using System;
using System.Collections.Generic;

namespace Nopnag.EventBusLib
{
  /// <summary>
  /// A hash set with intrusive insertion-order links. Lookup, add, and removal are
  /// average O(1); ordered traversal is O(n) and allocation-free after construction.
  /// </summary>
  internal sealed class OrderedListenerSet<T> where T : BusEvent
  {
    struct ListenerNode
    {
      public ListenerRegistration<T> Registration;
      public int HashCode;
      public int BucketNext;
      public int PreviousOrNextFree;
      public int OrderNext;
    }

    static readonly EqualityComparer<ListenerDelegate<T>> Comparer =
      EqualityComparer<ListenerDelegate<T>>.Default;

    int[] _buckets;
    ListenerNode[] _nodes;
    int _first = -1;
    int _last = -1;
    int _firstFree = -1;
    int _nextUnused;

    public OrderedListenerSet(int initialCapacity)
    {
      _buckets = new int[initialCapacity];
      _nodes = new ListenerNode[initialCapacity];
    }

    public ListenerRegistration<T> Find(ListenerDelegate<T> listener)
    {
      var hashCode = GetHashCode(listener);
      for (var current = _buckets[hashCode & (_buckets.Length - 1)] - 1;
           current != -1; current = _nodes[current].BucketNext)
        if (_nodes[current].HashCode == hashCode &&
            Comparer.Equals(_nodes[current].Registration.Callback, listener))
          return _nodes[current].Registration;
      return null;
    }

    public void Add(ListenerRegistration<T> registration)
    {
      var listener = registration.Callback;
      var hashCode = GetHashCode(listener);
      var bucket = hashCode & (_buckets.Length - 1);
      for (var current = _buckets[bucket] - 1;
           current != -1;
           current = _nodes[current].BucketNext)
      {
        if (_nodes[current].HashCode == hashCode &&
            Comparer.Equals(_nodes[current].Registration.Callback, listener))
          return;
      }

      var index = AllocateNode();
      bucket = hashCode & (_buckets.Length - 1);
      _nodes[index].Registration = registration;
      _nodes[index].HashCode = hashCode;
      _nodes[index].BucketNext = _buckets[bucket] - 1;
      _nodes[index].PreviousOrNextFree = _last;
      _nodes[index].OrderNext = -1;
      _buckets[bucket] = index + 1;

      if (_last == -1)
        _first = index;
      else
        _nodes[_last].OrderNext = index;

      _last = index;
    }

    public void Remove(ListenerRegistration<T> registration)
    {
      var listener = registration.Callback;
      var hashCode = GetHashCode(listener);
      var bucket = hashCode & (_buckets.Length - 1);
      var previousInBucket = -1;
      var index = _buckets[bucket] - 1;
      while (index != -1)
      {
        if (_nodes[index].HashCode == hashCode &&
            ReferenceEquals(_nodes[index].Registration, registration))
          break;

        previousInBucket = index;
        index = _nodes[index].BucketNext;
      }

      if (index == -1) return;

      if (previousInBucket == -1)
        _buckets[bucket] = _nodes[index].BucketNext + 1;
      else
        _nodes[previousInBucket].BucketNext = _nodes[index].BucketNext;

      var previous = _nodes[index].PreviousOrNextFree;
      var next = _nodes[index].OrderNext;

      if (previous == -1)
        _first = next;
      else
        _nodes[previous].OrderNext = next;

      if (next == -1)
        _last = previous;
      else
        _nodes[next].PreviousOrNextFree = previous;

      _nodes[index].Registration = null;
      registration.Detach();
      _nodes[index].HashCode = -1;
      _nodes[index].BucketNext = -1;
      _nodes[index].OrderNext = -1;
      _nodes[index].PreviousOrNextFree = _firstFree;
      _firstFree = index;
    }

    public void Clear()
    {
      for (var index = _first; index != -1; index = _nodes[index].OrderNext)
        _nodes[index].Registration.Detach();
      Array.Clear(_buckets, 0, _buckets.Length);
      Array.Clear(_nodes, 0, _nextUnused);
      _first = _last = _firstFree = -1;
      _nextUnused = 0;
    }

    public bool Raise(T @event)
    {
      var index = _first;
      while (index != -1)
      {
        var listener = _nodes[index].Registration.Callback;
        index = _nodes[index].OrderNext;
        listener(@event);
        if (@event.IsPropagationStopped) return false;
      }

      return true;
    }

    int AllocateNode()
    {
      if (_firstFree != -1)
      {
        var index = _firstFree;
        _firstFree = _nodes[index].PreviousOrNextFree;
        return index;
      }

      if (_nextUnused == _nodes.Length)
        Resize();

      return _nextUnused++;
    }

    void Resize()
    {
      var newCapacity = _nodes.Length * 2;
      Array.Resize(ref _nodes, newCapacity);
      _buckets = new int[newCapacity];

      for (var index = 0; index < _nextUnused; index++)
      {
        if (_nodes[index].HashCode < 0) continue;

        var bucket = _nodes[index].HashCode & (newCapacity - 1);
        _nodes[index].BucketNext = _buckets[bucket] - 1;
        _buckets[bucket] = index + 1;
      }
    }

    static int GetHashCode(ListenerDelegate<T> listener)
    {
      return listener == null ? 0 : Comparer.GetHashCode(listener) & 0x7fffffff;
    }
  }
}
