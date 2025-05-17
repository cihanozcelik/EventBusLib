using System;
using System.Collections.Generic;
using System.Linq;

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

  public static class EventBus
  {
    public static EventQuery<TEvent> Query<TEvent>() where TEvent : BusEvent
    {
      return EventBus<TEvent>.SelfQuery;
    }

    public static void Raise<TEvent>(TEvent busEvent) where TEvent : BusEvent
    {
      EventBus<TEvent>.Raise(busEvent);
    }
  }

  public static class EventBus<T> where T : BusEvent
  {
    public static EventQuery<T> SelfQuery;

    static EventBus()
    {
      if (SelfQuery == null) SelfQuery = new EventQuery<T>();
    }

    public static IIListener Listen(ListenerDelegate<T> listener)
    {
      return SelfQuery.Listen(listener);
    }

    public static void Raise(T @event)
    {
      if (SelfQuery == null) SelfQuery = new EventQuery<T>();
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

  public class EventQuery<T> where T : BusEvent
  {
    readonly Dictionary<Type, EventQuery<T>> _dictionary;
    readonly Dictionary<Type, EventQuery<T>> _genericDictionary;
    readonly HashSet<ListenerDelegate<T>> _hash;
    bool _isRaising;
    readonly Queue<Action> _operationQueue;

    public EventQuery()
    {
      _dictionary = new Dictionary<Type, EventQuery<T>>();
      _genericDictionary = new Dictionary<Type, EventQuery<T>>();
      _hash = new HashSet<ListenerDelegate<T>>();
      _operationQueue = new Queue<Action>();
    }

    public virtual IIListener Listen(ListenerDelegate<T> @event)
    {
      Action subscribeAction = () => _hash.Add(@event);
      if (_isRaising)
        _operationQueue.Enqueue(subscribeAction);
      else
        subscribeAction();

      return new Listener(() => UnsubscribeInternal(@event));
    }

    public virtual void Raise(T @event)
    {
      _isRaising = true;

      foreach (var listener in _hash)
      {
        listener(@event);
        if (@event.IsPropagationStopped)
        {
          _isRaising = false;
          ProcessOperationQueue();
          return;
        }
      }

      foreach (var type in _dictionary.Keys)
      {
        if (_dictionary.TryGetValue(type, out var eventQuery))
        {
          eventQuery.Raise(@event);
          if (@event.IsPropagationStopped)
          {
            _isRaising = false;
            ProcessOperationQueue();
            return;
          }
        }
      }

      foreach (var type in _genericDictionary.Keys)
      {
        if (_genericDictionary.TryGetValue(type, out var eventQuery))
        {
          eventQuery.Raise(@event);
          if (@event.IsPropagationStopped)
          {
            _isRaising = false;
            ProcessOperationQueue();
            return;
          }
        }
      }

      _isRaising = false;
      ProcessOperationQueue();
    }

    public EventQuery<T> Where<TParameterType>(in object value) where TParameterType : IParameter
    {
      var parameterType = typeof(TParameterType);
      ParameterQuery<T, TParameterType> pq;
      if (!_dictionary.ContainsKey(parameterType))
      {
        pq = new ParameterQuery<T, TParameterType>();
        _dictionary[parameterType] = pq;
        return pq.Where(value);
      }

      pq = (ParameterQuery<T, TParameterType>)_dictionary[parameterType];
      return pq.Where(value);
    }

    public EventQuery<T> Where<TParameterType>(in TParameterType value) where TParameterType : class
    {
      var parameterType = typeof(TParameterType);
      GenericParameterQuery<T, TParameterType> pq;
      if (!_genericDictionary.ContainsKey(parameterType))
      {
        pq = new GenericParameterQuery<T, TParameterType>();
        _genericDictionary[parameterType] = pq;
        return pq.Where(value);
      }

      pq = (GenericParameterQuery<T, TParameterType>)_genericDictionary[parameterType];
      return pq.Where(value);
    }

    void ProcessOperationQueue()
    {
      while (_operationQueue.Count > 0)
      {
        var action = _operationQueue.Dequeue();
        action();
      }
    }

    void UnsubscribeInternal(ListenerDelegate<T> @event)
    {
      Action unsubscribeAction = () => _hash.Remove(@event);
      if (_isRaising)
        _operationQueue.Enqueue(unsubscribeAction);
      else
        unsubscribeAction();
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
      var type = typeof(TParameterType);
      var value = @event.Get<TParameterType>();
      EventQuery<T> eventQuery;
      if (value != null && _valueDictionary.TryGetValue(value, out eventQuery))
        eventQuery.Raise(@event);
    }

    public EventQuery<T> Where(in object value)
    {
      if (!_valueDictionary.ContainsKey(value))
      {
        var eq = new EventQuery<T>();
        _valueDictionary[value] = eq;
      }

      return _valueDictionary[value];
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
      var type = typeof(TParameterType);
      var value = @event.GetGeneric<TParameterType>();
      EventQuery<T> eventQuery;
      if (value != null && _valueDictionary.TryGetValue(value, out eventQuery))
        eventQuery.Raise(@event);
    }

    public EventQuery<T> Where(in object value)
    {
      if (!_valueDictionary.ContainsKey(value))
      {
        var eq = new EventQuery<T>();
        _valueDictionary[value] = eq;
      }

      return _valueDictionary[value];
    }
  }
} 