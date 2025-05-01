# Nopnag EventBusLib

A flexible and queryable event bus system for C# and Unity3D.

## Overview

This EventBus implementation provides a straightforward way to decouple different parts of your application by using a publish-subscribe pattern. Instead of direct method calls, systems can raise events, and other systems can listen for specific events they are interested in, optionally filtering them based on event parameters.

## Key Features

*   **Type-Safe Events:** Uses generic event types (`BusEvent`) for compile-time safety.
*   **Parameter Querying:** Allows listeners to subscribe only to events that match specific parameter values or types. Parameter types implement `IIParameter` and can directly represent the filtered data (like an object reference or enum) or hold a value.
*   **Efficient Filtering:** Filtering logic is handled *internally* by the EventBus before invoking listeners. When an event is raised, the system efficiently finds only the relevant subscribers based on their `Where` clauses, leading to significantly better performance compared to manual checking in every listener.
*   **Decoupled Architecture:** Promotes cleaner code by reducing direct dependencies between components.
*   **Easy Unsubscription:** Provides a listener handle (`IIListener`) for easy removal of subscriptions.
*   **Static Access:** Simple API access via static `EventBus` class.

## How It Works

1.  **Define Events:** Create classes that inherit from `BusEvent`.
2.  **Define Parameters (Optional, for Filtering):** If you want to filter events based on parameters, define types (classes or structs) that implement the marker interface `IIParameter`. These types can directly inherit from relevant classes (like `Warrior` in the tests) or `object`.
3.  **Set Parameters (Optional):** Use `eventInstance.Set<ParameterType>(parameterValue)` to attach filterable parameters to an event instance. `parameterValue` is the actual data (instance, enum value, int, etc.) you want to filter by.
4.  **Raise Events:** Use `EventBus.Raise(yourEventInstance)` to publish an event. The EventBus efficiently routes the event only to matching listeners.
5.  **Listen to Events:** Use `EventBus.Listen<YourEventType>(handler)` to subscribe to all events of a specific type.
6.  **Filtered Listening:** Use `EventBus.Query<YourEventType>().Where<ParameterType>(filterValue).Listen(handler)` to subscribe only to events where the parameter of type `ParameterType` matches `filterValue`. Chaining `Where` clauses creates more specific, efficient subscriptions.
7.  **Access Parameters:** Inside your listener handler, use `eventInstance.Get<ParameterType>()` to retrieve the value of a parameter that was set via `Set<T>()`. The returned value will be the `parameterValue` originally passed to `Set`.
8.  **Unsubscribe:** Keep the `IIListener` returned by `Listen()` and call `listener.Unsubscribe()` when you no longer need to listen.

## Usage Examples (Based on Test Code Structure)

### 1. Define Event and Parameters

```csharp
using Nopnag.EventBus;

// --- Define Parameter Types (implementing IIParameter for filtering) ---

// Represents the source entity, inheriting from Warrior and implementing IIParameter
public class Source : Warrior, IIParameter { }

// Represents the destination entity
public class Destination : Warrior, IIParameter { }

// Represents the type of weapon used (could be an enum)
public enum Weapon { Sword, Axe, Bow }
public class WeaponType : object, IIParameter { }

// Represents a numerical amount
public class Amount : object, IIParameter { }

// --- Define an entity (used by parameters) ---
public class Warrior { /* ... warrior data ... */ }

// --- Define the Event --- 
public class CombatEvent : BusEvent
{
  // Non-filterable data can still go here
  public string LogMessage { get; set; }
}
```

### 2. Raising Events

```csharp
using Nopnag.EventBus;

// Assume warrior1, warrior2 exist
Warrior warrior1 = new Warrior();
Warrior warrior2 = new Warrior();
Weapon weaponUsed = Weapon.Sword;
int damageDealt = 15;

// Create the event instance
var combatEvent = new CombatEvent();
combatEvent.LogMessage = "Warrior 1 attacks Warrior 2 with Sword for 15 damage.";

// Set filterable parameters using Set<T>() with the actual values/references
combatEvent.Set<Source>(warrior1);
combatEvent.Set<Destination>(warrior2);
combatEvent.Set<WeaponType>(weaponUsed); // Set with the enum value
combatEvent.Set<Amount>(damageDealt);   // Set with the integer value

// Raise the event globally
EventBus.Raise(combatEvent);
```

### 3. Basic Listening (No Filters)

```csharp
using Nopnag.EventBus;
using UnityEngine; // For Debug.Log

public class CombatEventLogger : MonoBehaviour
{
  private IIListener _combatEventListener;

  void OnEnable()
  {
    _combatEventListener = EventBus.Listen<CombatEvent>(OnCombat);
  }

  void OnDisable()
  {
    _combatEventListener?.Unsubscribe();
  }

  void OnCombat(CombatEvent evt)
  {
    Debug.Log($"Combat Log: {evt.LogMessage}");
    // Access parameters using Get<ParameterType>()
    var source = (Warrior)evt.Get<Source>();
    var damage = (int)evt.Get<Amount>();
    var weapon = (Weapon)evt.Get<WeaponType>();

    // if (source != null) Debug.Log($"Source: {source}");
    // Debug.Log($"Damage: {damage}");
    // Debug.Log($"Weapon: {weapon}");
  }
}
```

### 4. Filtered Listening (Querying)

```csharp
using Nopnag.EventBus;
using UnityEngine;

public class SpecificCombatObserver : MonoBehaviour
{
  public Warrior warriorToObserve; // Assign this in the Inspector or code
  private IIListener _swordAttackListener;
  private IIListener _heavyDamageListener;

  void OnEnable()
  {
    if (warriorToObserve == null) return;

    // --- Example 1: Listen when warriorToObserve attacks using a Sword ---
    _swordAttackListener = EventBus.Query<CombatEvent>()
                                   .Where<Source>(warriorToObserve)   // Filter by source instance
                                   .Where<WeaponType>(Weapon.Sword) // Filter by weapon enum value
                                   .Listen(OnObservedWarriorSwordAttack);

    // --- Example 2: Listen when warriorToObserve receives exactly 15 damage ---
    _heavyDamageListener = EventBus.Query<CombatEvent>()
                                   .Where<Destination>(warriorToObserve) // Filter by destination instance
                                   .Where<Amount>(15)                 // Filter by amount value
                                   .Listen(OnObservedWarriorTookHeavyDamage);
  }

  void OnDisable()
  {
    _swordAttackListener?.Unsubscribe();
    _heavyDamageListener?.Unsubscribe();
  }

  void OnObservedWarriorSwordAttack(CombatEvent evt)
  {
    // Called ONLY when source is warriorToObserve AND weapon is Sword
    Debug.Log($"Observed warrior attacked with a sword! Log: {evt.LogMessage}");
  }

  void OnObservedWarriorTookHeavyDamage(CombatEvent evt)
  {
    // Called ONLY when destination is warriorToObserve AND amount is 15
    Debug.Log($"Observed warrior took 15 damage! Log: {evt.LogMessage}");
  }
}
```

### 5. Unsubscribing

```csharp
// Assuming MyEvent exists and listener was stored
// IIListener listener = EventBus.Listen<MyEvent>(HandleMyEvent);

// ... later, when no longer needed ...
// listener?.Unsubscribe();
```

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