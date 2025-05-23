# Nopnag EventBusLib

A flexible and queryable event bus system for C# and Unity3D.

## Overview

This EventBus implementation provides a straightforward way to decouple different parts of your application by using a publish-subscribe pattern. Instead of direct method calls, systems can raise events, and other systems can listen for specific events they are interested in, optionally filtering them based on event parameters.

The library supports both **static (global)** and **instance-based (local)** EventBus usage, giving you flexibility in how you structure your event communication.

## Key Features

*   **Type-Safe Events:** Uses generic event types (`BusEvent`) for compile-time safety.
*   **Parameter Querying:** Allows listeners to subscribe only to events that match specific parameter values or types. Parameter types implement `IParameter` and can directly represent the filtered data (like an object reference or enum) or hold a value.
*   **Efficient Filtering:** Filtering logic is handled *internally* by the EventBus before invoking listeners. When an event is raised, the system efficiently finds only the relevant subscribers based on their `Where` clauses, leading to significantly better performance compared to manual checking in every listener.
*   **Decoupled Architecture:** Promotes cleaner code by reducing direct dependencies between components.
*   **Easy Unsubscription:** Provides a listener handle (`IIListener`) for easy removal of subscriptions.
*   **Dual API Support:** Both static access via `EventBus` class and instance-based usage via `new LocalEventBus()`.
*   **Event Isolation:** Instance-based LocalEventBuses are completely isolated from each other and from the global static EventBus.

## API Overview

### Static API (Global EventBus)
```csharp
// Traditional static API - global event system
EventBus<MyEvent>.Listen(handler);
EventBus<MyEvent>.Where<Parameter>(value).Listen(handler);
EventBus.Raise(myEvent);
```

### Instance API (Local EventBus)
```csharp
// New instance-based API - create isolated LocalEventBus instances
var localBus = new LocalEventBus();
localBus.On<MyEvent>().Listen(handler);
localBus.On<MyEvent>().Where<Parameter>(value).Where<OtherParam>(otherValue).Listen(handler);
localBus.Raise(myEvent);
```

## How It Works

1.  **Define Events:** Create classes that inherit from `BusEvent`.
2.  **Define Parameters (Optional, for Filtering):** If you want to filter events based on parameters, define types (classes or structs) that implement the marker interface `IParameter`. These types can directly inherit from relevant classes (like `Warrior` in the tests) or `object`.
3.  **Set Parameters (Optional):** Use `eventInstance.Set<ParameterType>(parameterValue)` to attach filterable parameters to an event instance. `parameterValue` is the actual data (instance, enum value, int, etc.) you want to filter by.
4.  **Choose Your API:** Use either the static API for global events or create instance-based LocalEventBus for local/scoped events.
5.  **Raise Events:** Use `EventBus.Raise(yourEventInstance)` (static) or `localBus.Raise(yourEventInstance)` (instance) to publish an event.
6.  **Listen to Events:** Use `EventBus<YourEventType>.Listen(handler)` (static) or `localBus.On<YourEventType>().Listen(handler)` (instance) to subscribe to all events of a specific type.
7.  **Filtered Listening:** Use `Where<ParameterType>(filterValue)` for both static and instance APIs to subscribe only to events where the parameter matches. Chain multiple conditions for more specific subscriptions.
8.  **Access Parameters:** Inside your listener handler, use `eventInstance.Get<ParameterType>()` to retrieve the value of a parameter that was set via `Set<T>()`.
9.  **Unsubscribe:** Keep the `IIListener` returned by `Listen()` and call `listener.Unsubscribe()` when you no longer need to listen.

## Usage Examples

### 1. Define Event and Parameters

```csharp
using Nopnag.EventBusLib;

// --- Define Parameter Types (implementing IParameter for filtering) ---

// Represents the source entity, inheriting from Warrior and implementing IParameter
public class Source : Warrior, IParameter { }

// Represents the destination entity
public class Destination : Warrior, IParameter { }

// Represents the type of weapon used (could be an enum)
public enum Weapon { Sword, Axe, Bow }
public class WeaponType : object, IParameter { }

// Represents a numerical amount
public class Amount : object, IParameter { }

// --- Define an entity (used by parameters) ---
public class Warrior { /* ... warrior data ... */ }

// --- Define the Event --- 
public class CombatEvent : BusEvent
{
  // Non-filterable data can still go here
  public string LogMessage { get; set; }
}
```

### 2. Instance-Based LocalEventBus (Recommended for Local Scopes)

```csharp
using Nopnag.EventBusLib;
using UnityEngine;

public class CombatSystem : MonoBehaviour
{
    // Create a local EventBus for this combat system
    private readonly LocalEventBus _combatBus = new LocalEventBus();
    
    void Start()
    {
        // Subscribe to combat events in this local scope
        _combatBus.On<CombatEvent>().Listen(OnAnyCombat);
        
        // Subscribe with parameter filtering using Where
        _combatBus.On<CombatEvent>()
            .Where<WeaponType>(Weapon.Sword)
            .Where<Amount>(15)
            .Listen(OnSwordAttackWith15Damage);
    }
    
    void SimulateCombat()
    {
        var warrior1 = new Warrior();
        var warrior2 = new Warrior();
        
        // Create and configure the event
        var combatEvent = new CombatEvent();
        combatEvent.LogMessage = "Warrior 1 attacks Warrior 2 with Sword for 15 damage.";
        combatEvent.Set<Source>(warrior1);
        combatEvent.Set<Destination>(warrior2);
        combatEvent.Set<WeaponType>(Weapon.Sword);
        combatEvent.Set<Amount>(15);
        
        // Raise the event on the local bus
        _combatBus.Raise(combatEvent);
    }
    
    void OnAnyCombat(CombatEvent evt)
    {
        Debug.Log($"Combat occurred: {evt.LogMessage}");
    }
    
    void OnSwordAttackWith15Damage(CombatEvent evt)
    {
        Debug.Log("Specific sword attack with 15 damage detected!");
    }
}
```

### 3. Static EventBus (Global Events)

```csharp
using Nopnag.EventBusLib;
using UnityEngine;

public class GlobalCombatLogger : MonoBehaviour
{
    private IIListener _combatEventListener;

    void OnEnable()
    {
        // Subscribe to global combat events
        _combatEventListener = EventBus<CombatEvent>.Listen(OnCombat);
    }

    void OnDisable()
    {
        _combatEventListener?.Unsubscribe();
    }

    void OnCombat(CombatEvent evt)
    {
        Debug.Log($"Global Combat Log: {evt.LogMessage}");
        
        // Access parameters using Get<ParameterType>()
        var source = (Warrior)evt.Get<Source>();
        var damage = (int)evt.Get<Amount>();
        var weapon = (Weapon)evt.Get<WeaponType>();
    }
}

// Somewhere else in your code - raise global events
public class GameManager : MonoBehaviour
{
    void TriggerGlobalCombat()
    {
        var combatEvent = new CombatEvent();
        combatEvent.Set<WeaponType>(Weapon.Axe);
        
        // Raise on the global EventBus
        EventBus.Raise(combatEvent);
    }
}
```

### 4. Multiple Isolated LocalEventBus Instances

```csharp
public class MultiPlayerCombat : MonoBehaviour
{
    private readonly LocalEventBus _player1Bus = new LocalEventBus();
    private readonly LocalEventBus _player2Bus = new LocalEventBus();
    
    void Start()
    {
        // Each player has their own isolated event system
        _player1Bus.On<CombatEvent>().Listen(evt => Debug.Log("Player 1 combat"));
        _player2Bus.On<CombatEvent>().Listen(evt => Debug.Log("Player 2 combat"));
        
        // Events on player1Bus won't affect player2Bus and vice versa
        _player1Bus.Raise(new CombatEvent()); // Only Player 1 listener called
        _player2Bus.Raise(new CombatEvent()); // Only Player 2 listener called
    }
}
```

### 5. Backward Compatibility

```csharp
// All existing code continues to work unchanged
var listener = EventBus<CombatEvent>.Where<Source>(warrior1)
                                   .Where<WeaponType>(Weapon.Sword)
                                   .Listen(OnWarriorSwordAttack);

EventBus<CombatEvent>.Raise(combatEvent);
listener.Unsubscribe();
```

## API Comparison

| Feature | Static API | Instance API |
|---------|------------|--------------|
| **Scope** | Global | Local/Isolated |
| **Creation** | Automatic | `new LocalEventBus()` |
| **Subscribe** | `EventBus<T>.Listen()` | `bus.On<T>().Listen()` |
| **Filter** | `.Where<Param>(value)` | `.Where<Param>(value)` |
| **Raise** | `EventBus.Raise(event)` | `bus.Raise(event)` |
| **Use Case** | Global game events | Component-specific events |

## When to Use Which API

### Use Static API When:
- You need global, application-wide events
- You want simple, straightforward event communication
- You're working with singleton systems
- You need backward compatibility with existing code

### Use Instance API When:
- You need isolated event systems (e.g., per-player, per-level)
- You want to avoid global state
- You're building modular, testable components
- You need multiple independent event systems
- You want better control over event scope and lifetime

## Installation

You can install this package using the Unity Package Manager:

1.  Open the Package Manager (`Window` > `Package Manager`).
2.  Click the `+` button in the top-left corner.
3.  Select `Add package from git URL...`.
4.  Enter the repository URL: `https://github.com/cihanozcelik/EventBusLib.git`
5.  Click `Add`.

Alternatively, you can add it directly to your `manifest.json` file in the `Packages` folder:
```json
{
  "dependencies": {
    "com.nopnag.eventbuslib": "https://github.com/cihanozcelik/EventBusLib.git",
    // ... other dependencies
  }
}
```