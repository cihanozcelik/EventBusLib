using System;
using System.Collections.Generic;

namespace Nopnag.EventBusLib // Updated namespace
{

  // Interface for parameter types, assuming it's needed by BusEvent
  public interface IParameter {}

  public class BusEvent
  {
    readonly Dictionary<Type, object> _dict;
    readonly Dictionary<Type, object> _genericDict;

    public BusEvent()
    {
      _dict = new Dictionary<Type, object>();
      _genericDict = new Dictionary<Type, object>();
    }

    public virtual bool IsPropagationStopped { get; set; }

    public object Get<T>() where T : IParameter
    {
      object value;
      if (_dict.TryGetValue(typeof(T), out value)) return value;

      return default(T);
    }

    public T GetGeneric<T>() where T : class
    {
      object value;
      if (_genericDict.TryGetValue(typeof(T), out value)) return (T)value;

      return default;
    }

    public virtual void ResetPropagation()
    {
      IsPropagationStopped = false;
    }

    public BusEvent Set<T>(T value) where T : class
    {
      _genericDict[typeof(T)] = value;
      return this;
    }

    public BusEvent Set<T>(object value) where T : IParameter
    {
      _dict[typeof(T)] = value;
      return this;
    }

    public void StopPropagation()
    {
      IsPropagationStopped = true;
    }
  }
} 