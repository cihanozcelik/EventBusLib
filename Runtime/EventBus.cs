using System;
using System.Collections.Generic;
using System.Threading;

namespace Nopnag.EventBusLib // Updated namespace
{
  public delegate void ListenerDelegate<T>(T @event);

  public interface IIListener
  {
    void Unsubscribe();
  }

  public struct Listener : IIListener
  {
    readonly Action _unsubscribeAction;

    public Listener(Action unsubscribeAction)
    {
      _unsubscribeAction = unsubscribeAction;
    }

    public void Unsubscribe()
    {
      _unsubscribeAction();
    }
  }

  // Static EventBus API (unchanged for backward compatibility)
  public static class EventBus
  {
    static long _raiseIdCounter;
    // Separate from the shared raise-id counter: a first unobserved local raise
    // must not allocate a global registry merely by assigning its dispatch id.
    static class GlobalRoots
    {
      internal static readonly List<Action> Cleanups = new List<Action>();
    }

    internal static void RegisterGlobalRoot(Action cleanup) => GlobalRoots.Cleanups.Add(cleanup);

    /// <summary>
    /// Clears all initialized global event roots in place. Main-thread lifecycle operation:
    /// call after owners shut down or before preparing new owners, never during dispatch.
    /// Local buses and RaiseUniqueId are unaffected. Filter queries become invalid.
    /// </summary>
    public static void ClearAll()
    {
      if (EventDispatch.IsActive)
        throw new InvalidOperationException("EventBus.ClearAll cannot run during event dispatch.");
      for (var i = 0; i < GlobalRoots.Cleanups.Count; i++) GlobalRoots.Cleanups[i]();
    }

    internal static long NextRaiseUniqueId()
    {
      return Interlocked.Increment(ref _raiseIdCounter);
    }

    public static EventQuery<TEvent> Query<TEvent>() where TEvent : BusEvent
    {
      return EventBus<TEvent>.SelfQuery;
    }

    /// <summary>
    /// Raises an event globally. The outermost dispatch resets propagation and
    /// assigns a new RaiseUniqueId before invoking listeners.
    /// </summary>
    public static void Raise<TEvent>(TEvent busEvent) where TEvent : BusEvent
    {
      EventBus<TEvent>.Raise(busEvent);
    }
  }

  internal static class EventDispatch
  {
    static int _activeScopes;
    internal static bool IsActive => _activeScopes != 0;
    internal readonly struct Scope : IDisposable
    {
      readonly BusEvent _event;

      internal Scope(BusEvent busEvent)
      {
        if (busEvent == null) throw new ArgumentNullException(nameof(busEvent));
        _event = busEvent;
        var isDepthZero = busEvent.ActiveRaiseDepth == 0;
        busEvent.ActiveRaiseDepth++;
        _activeScopes++;
        try
        {
          if (isDepthZero)
          {
            busEvent.ResetPropagation();
            busEvent.RaiseUniqueId = EventBus.NextRaiseUniqueId();
          }
        }
        catch
        {
          busEvent.ActiveRaiseDepth--;
          _activeScopes--;
          throw;
        }
      }

      public void Dispose()
      {
        _event.ActiveRaiseDepth--;
        _activeScopes--;
      }
    }

    internal static Scope Enter(BusEvent busEvent)
    {
      return new Scope(busEvent);
    }

    internal static void RaiseUnobserved(BusEvent busEvent)
    {
      using (Enter(busEvent))
      {
      }
    }
  }

  public static class EventBus<T> where T : BusEvent
  {
    public static readonly EventQuery<T> SelfQuery;

    static EventBus()
    {
      SelfQuery = new EventQuery<T>();
      EventBus.RegisterGlobalRoot(SelfQuery.ClearGlobalRoot);
    }

    public static IIListener Listen(ListenerDelegate<T> listener)
    {
      return SelfQuery.Listen(listener);
    }

    /// <summary>
    /// Raises an event globally. The outermost dispatch resets propagation and
    /// assigns a new RaiseUniqueId before invoking listeners.
    /// </summary>
    public static void Raise(T @event)
    {
      SelfQuery.Raise(@event);
    }

    public static EventQuery<T> Where<TParameterType>(in object parameter)
      where TParameterType : IParameter
    {
      return SelfQuery.Where<TParameterType>(parameter);
    }

    public static EventQuery<T> Where<TParameterType>(in TParameterType parameter)
      where TParameterType : class
    {
      return SelfQuery.Where(parameter);
    }
  }

  // New instance-based LocalEventBus
  public class LocalEventBus
  {
    private readonly Dictionary<Type, object> _eventQueries = new Dictionary<Type, object>();

    public LocalEventBus()
    {
    }

    // Instance API
    public EventQuery<TEvent> On<TEvent>() where TEvent : BusEvent
    {
      var eventType = typeof(TEvent);
      if (!_eventQueries.ContainsKey(eventType))
      {
        _eventQueries[eventType] = new EventQuery<TEvent>();
      }
      return (EventQuery<TEvent>)_eventQueries[eventType];
    }

    /// <summary>
    /// Raises an event on this local bus. The outermost dispatch resets propagation
    /// and assigns a new RaiseUniqueId before invoking listeners.
    /// </summary>
    public void Raise<TEvent>(TEvent busEvent) where TEvent : BusEvent
    {
      if (_eventQueries.TryGetValue(typeof(TEvent), out var query))
      {
        ((EventQuery<TEvent>)query).Raise(busEvent);
        return;
      }

      EventDispatch.RaiseUnobserved(busEvent);
    }
  }

  public class EventQuery<T> where T : BusEvent
  {
    const int InitialCapacity = 4;

    enum PendingOperationType : byte
    {
      Subscribe,
      Unsubscribe
    }

    struct PendingOperation
    {
      public PendingOperationType Type;
      public ListenerRegistration<T> Registration;

      public PendingOperation(PendingOperationType type, ListenerRegistration<T> registration)
      {
        Type = type;
        Registration = registration;
      }
    }

    readonly Dictionary<Type, EventQuery<T>> _dictionary;
    readonly Dictionary<Type, EventQuery<T>> _genericDictionary;
    readonly List<EventQuery<T>> _orderedQueries;
    readonly List<EventQuery<T>> _orderedGenericQueries;
    readonly OrderedListenerSet<T> _listeners;
    PendingOperation[] _pendingOperations;
    int _pendingOperationCount;
    int _raiseDepth;
    bool _invalidated;

    public EventQuery()
    {
      _dictionary = new Dictionary<Type, EventQuery<T>>();
      _genericDictionary = new Dictionary<Type, EventQuery<T>>();
      _orderedQueries = new List<EventQuery<T>>();
      _orderedGenericQueries = new List<EventQuery<T>>();
      _listeners = new OrderedListenerSet<T>(InitialCapacity);
      _pendingOperations = new PendingOperation[InitialCapacity];
    }

    /// <summary>
    /// Registers a listener once on this query. Listeners are invoked in registration
    /// order. Registering the same delegate again has no effect; unsubscribing and then
    /// registering it again appends it to the end. Mutations requested while this query
    /// is dispatching are applied after its outermost dispatch completes.
    /// </summary>
    public virtual IIListener Listen(ListenerDelegate<T> listener)
    {
      RequireValid();
      if (listener == null) throw new ArgumentNullException(nameof(listener));
      var registration = _listeners.Find(listener);
      // Resolve the eventual registration without changing the active traversal.
      for (var i = 0; i < _pendingOperationCount; i++)
      {
        var operation = _pendingOperations[i];
        if (!Equals(operation.Registration.Callback, listener)) continue;
        if (operation.Type == PendingOperationType.Subscribe)
          registration = operation.Registration;
        else if (ReferenceEquals(registration, operation.Registration))
          registration = null;
      }
      if (registration != null) return registration;
      registration = new ListenerRegistration<T>(this, listener);
      if (_raiseDepth > 0)
        EnqueueOperation(PendingOperationType.Subscribe, registration);
      else
        _listeners.Add(registration);
      return registration;
    }

    /// <summary>
    /// Dispatches an event through this query. The outermost query dispatch resets
    /// propagation and assigns a new RaiseUniqueId. Nested query dispatch preserves both.
    /// Direct listeners run in registration order, followed by IParameter filter branches
    /// and then class filter branches, each in first-definition order.
    /// </summary>
    public virtual void Raise(T @event)
    {
      RequireValid();
      using (EventDispatch.Enter(@event))
      {
        _raiseDepth++;
        try
        {
          if (!_listeners.Raise(@event)) return;

          var queryCount = _orderedQueries.Count;
          for (var i = 0; i < queryCount; i++)
          {
            _orderedQueries[i].Raise(@event);
            if (@event.IsPropagationStopped)
            {
              return;
            }
          }

          var genericQueryCount = _orderedGenericQueries.Count;
          for (var i = 0; i < genericQueryCount; i++)
          {
            _orderedGenericQueries[i].Raise(@event);
            if (@event.IsPropagationStopped)
            {
              return;
            }
          }
        }
        finally
        {
          _raiseDepth--;
          if (_raiseDepth == 0) ProcessPendingOperations();
        }
      }
    }

    /// <summary>
    /// Gets the query for an IParameter value. Parameter-type branches are dispatched
    /// deterministically in the order in which each type was first defined.
    /// </summary>
    public EventQuery<T> Where<TParameterType>(in object value) where TParameterType : IParameter
    {
      RequireValid();
      var parameterType = typeof(TParameterType);
      ParameterQuery<T, TParameterType> pq;
      if (!_dictionary.ContainsKey(parameterType))
      {
        pq = new ParameterQuery<T, TParameterType>();
        _dictionary[parameterType] = pq;
        _orderedQueries.Add(pq);
        return pq.Where(value);
      }

      pq = (ParameterQuery<T, TParameterType>)_dictionary[parameterType];
      return pq.Where(value);
    }

    /// <summary>
    /// Gets the query for a class parameter value. Class-parameter branches are dispatched
    /// deterministically in the order in which each type was first defined.
    /// </summary>
    public EventQuery<T> Where<TParameterType>(in TParameterType value) where TParameterType : class
    {
      RequireValid();
      var parameterType = typeof(TParameterType);
      GenericParameterQuery<T, TParameterType> pq;
      if (!_genericDictionary.ContainsKey(parameterType))
      {
        pq = new GenericParameterQuery<T, TParameterType>();
        _genericDictionary[parameterType] = pq;
        _orderedGenericQueries.Add(pq);
        return pq.Where(value);
      }

      pq = (GenericParameterQuery<T, TParameterType>)_genericDictionary[parameterType];
      return pq.Where(value);
    }

    void ProcessPendingOperations()
    {
      var operationCount = _pendingOperationCount;
      _pendingOperationCount = 0;
      for (var i = 0; i < operationCount; i++)
      {
        var operation = _pendingOperations[i];
        _pendingOperations[i] = default(PendingOperation);
        if (operation.Type == PendingOperationType.Subscribe)
          _listeners.Add(operation.Registration);
        else
          _listeners.Remove(operation.Registration);
      }
    }

    void EnqueueOperation(PendingOperationType type, ListenerRegistration<T> registration)
    {
      if (_pendingOperationCount == _pendingOperations.Length)
        Array.Resize(ref _pendingOperations, _pendingOperations.Length * 2);

      _pendingOperations[_pendingOperationCount++] = new PendingOperation(type, registration);
    }

    internal void UnsubscribeInternal(ListenerRegistration<T> registration)
    {
      if (_raiseDepth > 0)
        EnqueueOperation(PendingOperationType.Unsubscribe, registration);
      else
        _listeners.Remove(registration);
    }

    protected void RequireValid()
    {
      if (_invalidated)
        throw new ObjectDisposedException(GetType().Name, "This filter query was invalidated by EventBus.ClearAll. Prepare a new query.");
    }

    internal void ClearGlobalRoot() => ClearTree(false);

    internal virtual void ClearTree(bool invalidate)
    {
      for (var i = 0; i < _orderedQueries.Count; i++) _orderedQueries[i].ClearTree(true);
      for (var i = 0; i < _orderedGenericQueries.Count; i++) _orderedGenericQueries[i].ClearTree(true);
      _dictionary.Clear();
      _genericDictionary.Clear();
      _orderedQueries.Clear();
      _orderedGenericQueries.Clear();
      _listeners.Clear();
      for (var i = 0; i < _pendingOperationCount; i++) _pendingOperations[i].Registration.Detach();
      Array.Clear(_pendingOperations, 0, _pendingOperationCount);
      _pendingOperationCount = 0;
      _invalidated = invalidate;
    }
  }

  // A registration object is its identity. Duplicate Listen calls share this identity;
  // a later registration of the same delegate always gets a different object.
  internal sealed class ListenerRegistration<T> : IIListener where T : BusEvent
  {
    EventQuery<T> _query;
    bool _unsubscribeRequested;
    internal ListenerDelegate<T> Callback { get; private set; }

    internal ListenerRegistration(EventQuery<T> query, ListenerDelegate<T> callback)
    {
      _query = query;
      Callback = callback;
    }

    public void Unsubscribe()
    {
      if (_query == null || _unsubscribeRequested) return;
      _unsubscribeRequested = true;
      _query.UnsubscribeInternal(this);
    }

    internal void Detach()
    {
      _query = null;
      Callback = null;
    }
  }

  public class ParameterQuery<T, TParameterType> : EventQuery<T>
    where T : BusEvent where TParameterType : IParameter
  {
    readonly Dictionary<object, EventQuery<T>> _valueDictionary;

    public ParameterQuery()
    {
      _valueDictionary = new Dictionary<object, EventQuery<T>>();
    }

    public override void Raise(T @event)
    {
      RequireValid();
      using (EventDispatch.Enter(@event))
      {
        var type = typeof(TParameterType);
        var value = @event.Get<TParameterType>();
        EventQuery<T> eventQuery;
        if (value != null && _valueDictionary.TryGetValue(value, out eventQuery))
          eventQuery.Raise(@event);
      }
    }

    public EventQuery<T> Where(in object value)
    {
      RequireValid();
      if (!_valueDictionary.ContainsKey(value))
      {
        var eq = new EventQuery<T>();
        _valueDictionary[value] = eq;
      }

      return _valueDictionary[value];
    }

    internal override void ClearTree(bool invalidate)
    {
      foreach (var child in _valueDictionary.Values) child.ClearTree(true);
      _valueDictionary.Clear();
      base.ClearTree(invalidate);
    }
  }

  public class GenericParameterQuery<T, TParameterType> : EventQuery<T>
    where T : BusEvent where TParameterType : class
  {
    readonly Dictionary<object, EventQuery<T>> _valueDictionary;

    public GenericParameterQuery()
    {
      _valueDictionary = new Dictionary<object, EventQuery<T>>();
    }

    public override void Raise(T @event)
    {
      RequireValid();
      using (EventDispatch.Enter(@event))
      {
        var type = typeof(TParameterType);
        var value = @event.GetGeneric<TParameterType>();
        EventQuery<T> eventQuery;
        if (value != null && _valueDictionary.TryGetValue(value, out eventQuery))
          eventQuery.Raise(@event);
      }
    }

    public EventQuery<T> Where(in object value)
    {
      RequireValid();
      if (!_valueDictionary.ContainsKey(value))
      {
        var eq = new EventQuery<T>();
        _valueDictionary[value] = eq;
      }

      return _valueDictionary[value];
    }

    internal override void ClearTree(bool invalidate)
    {
      foreach (var child in _valueDictionary.Values) child.ClearTree(true);
      _valueDictionary.Clear();
      base.ClearTree(invalidate);
    }
  }
}
