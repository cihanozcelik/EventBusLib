# Nopnag EventBusLib

EventBusLib is a synchronous, typed publish/subscribe library for C# and Unity. It
provides one static global bus per closed event type, isolated instance-based local
buses, deterministic listener ordering, query trees for value routing, propagation
control, and a per-dispatch identifier.

This README documents the supported public API for EventBusLib `1.1.0`. Open
implementation defects and unsafe advanced paths are tracked separately in
[KNOWN_ISSUES.md](KNOWN_ISSUES.md). A known issue is not a supported usage
recommendation.

## Quick navigation

- [Define and reuse events](#start-with-typed-event-fields)
- [Choose global or local scope](#choose-event-scope-explicitly)
- [Own listener lifetime](#listener-ownership)
- [Understand exact event types](#exact-event-type-routing)
- [Use query routing](#query-routing-two-distinct-parameter-systems)
- [Dispatch and propagation](#dispatch-order)
- [Public API reference](#public-api-reference)
- [Allocation contract](#allocation-and-performance-contract)
- [Attention checklist](#attention-checklist)

## Installation

| Package | Value |
|---|---|
| Unity package name | `com.nopnag.eventbuslib` |
| Package version | `1.1.0` |
| Declared minimum Unity version | `6000.1` |
| Runtime namespace | `Nopnag.EventBusLib` |
| Runtime assembly | `Nopnag.EventBusLib.Runtime` |

Install the package through Unity Package Manager with the repository URL, or add an
exact tested revision to the consuming project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.nopnag.eventbuslib":
      "https://github.com/cihanozcelik/EventBusLib.git#<tested-revision>"
  }
}
```

Do not follow a moving branch in a production project without retesting the API and
allocation paths used by that project.

## Start with typed event fields

Event types inherit `BusEvent`. Put ordinary payload in strongly typed fields or
properties. Use query parameters only for values that must select a listener branch.

```csharp
using Nopnag.EventBusLib;

public sealed class DamageAppliedEvent : BusEvent
{
    public int Amount;
    public int TargetId;
    public bool Critical;
}
```

Construct and own reusable event instances at an initialization boundary:

```csharp
private readonly DamageAppliedEvent _damageApplied = new DamageAppliedEvent();

private void PublishDamage(int amount, int targetId, bool critical)
{
    _damageApplied.Amount = amount;
    _damageApplied.TargetId = targetId;
    _damageApplied.Critical = critical;
    EventBus.Raise(_damageApplied);
}
```

Publication is synchronous. Every matching listener finishes, propagation stops, or
an exception escapes before `Raise` returns. A publisher must not mutate or reuse the
same event concurrently or while it is still being synchronously dispatched.

## Choose event scope explicitly

### Static global bus

Use the global bus only for events whose scope is genuinely application-wide.

```csharp
private IIListener _listener;

private void Initialize()
{
    _listener = EventBus<GamePausedEvent>.Listen(OnGamePaused);
}

private void Shutdown()
{
    _listener.Unsubscribe();
    _listener = null;
}

private static void OnGamePaused(GamePausedEvent evt) { }

private static void Publish(GamePausedEvent evt)
{
    EventBus.Raise(evt);
    // Equivalent when the concrete type is explicit:
    // EventBus<GamePausedEvent>.Raise(evt);
}
```

Each closed `EventBus<T>` owns one static root `EventQuery<T>`. Listener delegates stay
there until unsubscribed or `EventBus.ClearAll()` runs. Unsubscribe alone retains
filter keys; ClearAll releases all global filter keys and subscriptions in place.
The root identity remains stable, while every old filter query becomes invalid.

### Global lifecycle cleanup

`EventBus.ClearAll()` is a main-thread lifecycle operation, after old owners finish
shutdown or before new owners prepare. It clears every initialized global event type;
there are no persistent event types or opt-out flags. It never clears local buses,
resets RaiseUniqueId, or replaces global roots. Initialized generic types register
one cleanup delegate during their first preparation; no reflection is used.

Unity integration calls it at SubsystemRegistration before scene preparation and
at EnteredEditMode after Play teardown. It does not run on individual scene unloads.
Scene reload must remain enabled for consumers that reconstruct scene-owned graphs.
Domain-reload-disabled Play still needs application resource and lifecycle tests.

ClearAll rejects any active dispatch (including local and nested dispatch) before
mutating any root. It must not be called from a listener or ResetPropagation override.
Retained filtered queries throw ObjectDisposedException on Listen, Where, or Raise;
prepare new filters for the next session. Root queries remain usable. Old listener
handles become inert and release their callback and query references, even if a
caller retains the handle. Repeated cleanup is supported. External references to
payload objects and event parameter dictionaries remain the caller's responsibility.

Normal raises reuse prepared storage. Root and registration construction, rebuilding
filters, and first registration of each generic event type belong to preparation.
Cleanup and subsequent preparation are not interactive gameplay operations.

### Instance-based local bus

Use `LocalEventBus` when a component, character, weapon, encounter, map, run, or test
owns an isolated event scope.

```csharp
public sealed class EncounterEvents
{
    private readonly LocalEventBus _bus = new LocalEventBus();
    private IIListener _listener;

    public void Initialize()
    {
        _listener = _bus.On<EnemyDefeatedEvent>().Listen(OnEnemyDefeated);
    }

    public void Publish(EnemyDefeatedEvent evt)
    {
        _bus.Raise(evt);
    }

    public void Shutdown()
    {
        _listener.Unsubscribe();
        _listener = null;
    }

    private static void OnEnemyDefeated(EnemyDefeatedEvent evt) { }
}
```

Different local bus instances have independent listener/query topologies and do not
deliver to each other or to the static bus. They still share the global
`RaiseUniqueId` counter, and nested publication of the same event instance shares that
instance's propagation/depth state. Do not describe local buses as isolated beyond
delivery topology.

`LocalEventBus` has no `Dispose` or bulk-clear method. A local lifetime owner must
unsubscribe its handles and release the bus and handles together.

## Listener ownership

`Listen` returns `IIListener`. The lifetime that registers a listener owns that handle
and must unsubscribe it before ending.

```csharp
private IIListener _spawnedListener;

private void BeginMap(LocalEventBus mapBus)
{
    _spawnedListener = mapBus.On<EnemySpawnedEvent>().Listen(OnEnemySpawned);
}

private void EndMap()
{
    _spawnedListener.Unsubscribe();
    _spawnedListener = null;
}
```

Listener semantics:

- The same delegate registered more than once on the same query is stored once.
- The same delegate registered on different queries is independent and can run once
  per matching query.
- Unsubscribe removes only the registration identified by its handle.
- Unsubscribe followed by a new subscription appends the delegate to the end of that
  query's order.
- Handles returned by Listen identify a registration, not only a delegate. Repeated
  Unsubscribe is harmless; an old handle cannot remove a later registration of the
  same delegate, including after ClearAll. Duplicate Listen calls share a registration
  (not reference counting); unsubscribing either handle ends that registration.
- Registrations removed during dispatch remain callable until that query's outermost
  dispatch completes, preserving existing deferred-mutation ordering.
- A default `Listener` or `Listener(null)` is invalid and throws on unsubscribe.

Avoid anonymous runtime lambdas when ownership and allocation matter. Store a stable
method-group delegate and its one handle.

## Exact event-type routing

Routing is by the exact closed generic event type:

- `EventBus<BaseEvent>` and `EventBus<DerivedEvent>` are separate roots.
- `LocalEventBus.On<BaseEvent>()` and `On<DerivedEvent>()` are separate queries.
- Publishing a derived instance as `BaseEvent` routes it as `BaseEvent`.
- There is no inheritance fan-out.

Both `BusEvent.Set` overloads return `BusEvent`, not the concrete subtype. Do not use a
fluent `Set` expression directly as the argument to `EventBus.Raise`, because generic
type inference can route it as `BusEvent`:

```csharp
// Correct: concrete type remains explicit.
DamageAppliedEvent evt = _damageApplied;
evt.Set<DamageRoute>(routeToken);
EventBus.Raise(evt);

// Do not do this:
// EventBus.Raise(new DamageAppliedEvent().Set<DamageRoute>(routeToken));
```

See [EB-003](KNOWN_ISSUES.md#eb-003--fluent-set-can-erase-the-derived-event-type-used-for-routing).

## Query routing: two distinct parameter systems

EventBusLib has two overload families backed by two independent dictionaries and query
trees. Choose one family for each route type and use it consistently.

### `IParameter` marker routing

Define a reference-type marker used as the type key. The stored route value is an
`object` and can be a stable token, identifier object, enum box, or other value.

```csharp
public sealed class TargetRoute : IParameter { }

private static readonly object PlayerTarget = new object();
private readonly DamageAppliedEvent _event = new DamageAppliedEvent();
private IIListener _listener;

private void Initialize()
{
    EventQuery<DamageAppliedEvent> playerDamage =
        EventBus<DamageAppliedEvent>.Where<TargetRoute>(PlayerTarget);

    _listener = playerDamage.Listen(OnPlayerDamage);
    _event.Set<TargetRoute>(PlayerTarget);
}

private void Publish()
{
    EventBus.Raise(_event);
}

private static void OnPlayerDamage(DamageAppliedEvent evt)
{
    object route = evt.Get<TargetRoute>();
}
```

The relevant APIs are:

- `BusEvent.Set<TParameter>(object value) where TParameter : IParameter`
- `BusEvent.Get<TParameter>() where TParameter : IParameter`
- `EventBus<TEvent>.Where<TParameter>(object value)`
- `EventQuery<TEvent>.Where<TParameter>(object value)`

Use class marker types. Struct `IParameter` markers currently have incorrect
missing-value behavior and can allocate during dispatch; see
[EB-001](KNOWN_ISSUES.md#eb-001--an-absent-struct-iparameter-can-match-its-default-value-route).

Passing an `int`, `float`, `bool`, enum, struct, or another value type through the
`object` API boxes it. Repeating `Set` with a value type can allocate on every
publication preparation. For a bounded route set, cache the boxed values once:

```csharp
public sealed class DamageBandRoute : IParameter { }

private static readonly object HeavyDamage = 2;

private void Initialize()
{
    _event.Set<DamageBandRoute>(HeavyDamage);
    EventBus<DamageAppliedEvent>
        .Where<DamageBandRoute>(HeavyDamage)
        .Listen(OnHeavyDamage);
}
```

### Class-type routing

The class overload stores and retrieves the value through a separate generic class
dictionary:

```csharp
public sealed class EncounterRoute
{
    public readonly int Id;

    public EncounterRoute(int id)
    {
        Id = id;
    }
}

private readonly EncounterRoute _route = new EncounterRoute(7);

private void Initialize()
{
    _event.Set<EncounterRoute>(_route);

    EventBus<DamageAppliedEvent>
        .Where<EncounterRoute>(_route)
        .Listen(OnEncounterDamage);
}

private static void OnEncounterDamage(DamageAppliedEvent evt)
{
    EncounterRoute route = evt.GetGeneric<EncounterRoute>();
}
```

The relevant APIs are:

- `BusEvent.Set<TClass>(TClass value) where TClass : class`
- `BusEvent.GetGeneric<TClass>() where TClass : class`
- `EventBus<TEvent>.Where<TClass>(TClass value)`
- `EventQuery<TEvent>.Where<TClass>(TClass value)`

Do not make a class route type also implement `IParameter`. Static argument types can
then make `Set` and `Where` select different stores and silently fail to match. See
[EB-002](KNOWN_ISSUES.md#eb-002--class--iparameter-types-can-silently-select-different-route-stores).

### Query chaining

`Where` returns another `EventQuery<T>`, so filters can be chained:

```csharp
EventQuery<DamageAppliedEvent> query = localBus
    .On<DamageAppliedEvent>()
    .Where<TargetRoute>(PlayerTarget)
    .Where<EncounterRoute>(_route);

IIListener listener = query.Listen(OnSpecificDamage);
```

A chain is an AND path through the query tree. The event must carry the matching value
for every level. Chain order is structural: `A -> B` and `B -> A` are different query
paths. Define one canonical ordering so one logical listener is not registered through
multiple equivalent paths.

### Equality, null, and route persistence

Route dictionaries use the route value's default `Equals` and `GetHashCode` behavior.
Reference identity is not guaranteed for types that override equality. Route keys must
be immutable and have stable equality/hash behavior after registration.

Null is not a supported route value:

- `Set` can store null, but filtered dispatch skips it.
- `Where(..., null)` throws because dictionaries reject a null key.
- Absent and explicitly null routes are not distinguishable.

Event route dictionaries persist for the entire event-object lifetime. There is no
parameter remove or clear API. A reused event must overwrite every route whose value
can change before publishing again. `ResetPropagation` does not clear payload or
routes.

Query keys and branch objects also persist after listeners unsubscribe. Use a bounded,
stable route-key set; do not build queries from unbounded runtime values.

## Dispatch order

For every `EventQuery<T>` level, dispatch order is deterministic:

1. Direct listeners on that query, in successful registration order.
2. `IParameter` type branches, in the order each parameter type was first defined on
   that query.
3. Class-parameter type branches, in the order each class type was first defined.
4. At each matching child query, the same ordering repeats recursively.

Only one value child under a parameter-type branch is selected because a `BusEvent`
stores at most one value per parameter type.

Filter dictionary enumeration order is not used for traversal. Listener storage is
set-like and insertion ordered. Dispatch complexity depends on invoked listeners and
the filter-type branches inspected along the traversed query levels.

## Propagation and `RaiseUniqueId`

At the beginning of every top-level raise of an event instance, EventBusLib:

1. Detects depth zero and increments the event's active raise depth.
2. Calls `ResetPropagation()`.
3. Assigns a new process-wide `RaiseUniqueId` and begins query traversal.

```csharp
EventBus.Raise(reusableEvent);
EventBus.Raise(reusableEvent); // new ID; stopped state reset automatically
```

`StopPropagation()` sets `IsPropagationStopped`. It stops later direct listeners on
the current query and prevents traversal into later filter branches on that path. The
flag remains set after `Raise` returns; the next separate top-level raise resets it.

Nested filtered-query traversal is part of the same raise and keeps the same ID.
Manual `ResetPropagation` is only needed when changing propagation state outside
normal dispatch.

`RaiseUniqueId` is a transient dispatch discriminator, not a persistent identity. The
property is zero on a newly constructed event until its first top-level raise. The
counter is shared by all global/local buses and event types and has no defined overflow
policy.

## Reentrancy

Listeners may synchronously raise a different event instance. A different event gets
its own top-level ID and propagation state.

Do not synchronously raise the same event instance again. Its `ActiveRaiseDepth`, ID,
and propagation flag are stored on the event object and are shared by the nested call,
even when the nested call uses another local or global bus. A nested stop can affect
the outer traversal, and an unguarded same-query re-raise can recurse until stack
overflow.

See [EB-008](KNOWN_ISSUES.md#eb-008--same-event-reentrant-raises-share-propagation-and-id-across-buses).

## Subscription and topology mutation during dispatch

When `Listen` or `Unsubscribe` targets the query currently being dispatched, the
operation is queued and applied after the outermost active dispatch of that query
finishes. Requests are applied in request order. Therefore:

- a newly subscribed listener does not join that active query traversal;
- a listener unsubscribed during that traversal can still run later in the same
  traversal;
- a guarded recursive raise of the same query does not see its queued subscription;
- pending operations are processed when listener exceptions unwind through the
  query's `finally` block.

Deferral is per query, not per overall event. Mutating a different query that has not
started dispatching can affect the same overall raise. `Where` topology construction
is never deferred and can have phase-dependent results.

Supported usage is to build every query and listener before publication begins. Do
not mutate subscriptions or call `Where` from runtime listeners.

## Exception behavior

Listener and user equality/hash exceptions propagate synchronously to the `Raise`
caller. EventBusLib does not catch, aggregate, log, or continue to later listeners.
Remaining listeners and branches are skipped.

Dispatch depth and pending operations are normally restored by `finally`. A throwing
ResetPropagation override unwinds both event depth and the active-dispatch guard
before propagating its exception. Overrides must still be side-effect-free.

Null events and listeners are required setup errors and now throw ArgumentNullException
before dispatch or registration. Invalidated filter queries reject use before mutation.

## Public API reference

### `IParameter`

Marker interface for the object-backed route family. Supported application usage is a
reference-type marker that identifies one route dimension.

### `BusEvent`

| Member | Purpose |
|---|---|
| constructor | Allocates marker and class route dictionaries. |
| `RaiseUniqueId` | Public read/internal write ID assigned at top-level raise. |
| virtual `IsPropagationStopped` | Current event-instance propagation flag. |
| `StopPropagation` | Stops later traversal on the current dispatch path. |
| virtual `ResetPropagation` | Clears the stop flag; called automatically. |
| `Set<T>(object)` where `T : IParameter` | Set one marker-route value. |
| `Get<T>()` where `T : IParameter` | Read marker-route value as object. |
| `Set<T>(T)` where `T : class` | Set one class-route value. |
| `GetGeneric<T>()` where `T : class` | Read one class-route value. |

`ActiveRaiseDepth` and the ID setter are internal. There is no payload/route clear API.

### `EventBus`

| Member | Purpose |
|---|---|
| `Query<TEvent>()` | Return the global root query for the exact event type. |
| `Raise<TEvent>(TEvent)` | Publish through the exact global event root. |
| `ClearAll()` | Clear all initialized global roots in place at a lifecycle boundary. |

### `EventBus<TEvent>`

| Member | Purpose |
|---|---|
| `Listen` | Register a direct global listener. |
| `Raise` | Publish through this exact global root. |
| marker/class `Where` overloads | Get or create a routed query branch. |
| public readonly `SelfQuery` field | Stable global root; cannot be replaced. |

SelfQuery is now readonly. Source code that assigned the old mutable field must use
ClearAll at a lifecycle boundary instead; precompiled consumers must be rebuilt.

### `LocalEventBus`

| Member | Purpose |
|---|---|
| constructor | Creates an empty isolated event-type registry. |
| `On<TEvent>()` | Get or lazily create the exact local root query. |
| `Raise<TEvent>(TEvent)` | Publish through an existing local root, or dispatch unobserved without creating one. |

The first `On<T>` for an event type allocates its root query. Raising an event type
that has no local root still resets propagation and assigns a new `RaiseUniqueId`, but
does not create query topology. Listener registration calls `On<T>` and therefore owns
that topology creation before publication.

### `EventQuery<TEvent>`

| Member | Purpose |
|---|---|
| constructor | Creates an independent query root and backing collections. |
| virtual `Listen` | Register a direct listener and return its handle. |
| virtual `Raise` | Dispatch beginning at this query. |
| marker/class `Where` overloads | Create or retrieve child route branches. |

Normal code obtains a root from a bus, uses leaf queries for `Listen`, and publishes
through the owning bus root. Raising a leaf directly bypasses ancestor filters.

### `ParameterQuery` and `GenericParameterQuery`

These public classes implement internal marker/class route levels. Their constructors,
`Raise`, and value `Where` methods are public, but direct consumer construction is an
unsafe compatibility surface: inherited listeners on the parameter-query object are
not dispatched by its override. Use bus/query `Where` methods instead. See
[EB-013](KNOWN_ISSUES.md#eb-013--public-parameter-query-types-are-unsafe-to-construct-and-raise-directly).

### Listener types

- `ListenerDelegate<T>` is the callback delegate type.
- `IIListener.Unsubscribe()` is the ownership interface returned by `Listen`.
- `Listen` returns an internal registration object whose identity and lifetime are
  tracked by its query. Removed registrations release their callback references.
- `Listener` remains a legacy public struct accepting an unsubscribe Action; manually
  constructed instances do not gain the registration-identity guarantees.

Prefer the returned `IIListener`; do not manually construct `Listener` values.

## Allocation and performance contract

EventBusLib does not promise that arbitrary API use is allocation-free.

The tested allocation-free path is narrow: a concrete event/query type has been
initialized, route branches and listeners are registered, storage has reached required
capacity, a reusable event with stable reference route values has been prepared, the
exact path has been warmed, and only repeated `Raise` is measured.

Known allocation points include:

- every `BusEvent` constructor: two dictionaries;
- every `EventQuery` constructor: dictionaries, lists, listener arrays, and pending
  operation storage;
- first static closed-generic use and first `LocalEventBus.On<T>`;
- each new registration from `Listen`: one handle object and possible storage growth;
  duplicate Listen on an existing registration returns its existing handle;
- every new `Where` type/value branch and dictionary/list growth;
- the fifth simultaneous pending mutation on one active query and later buffer growth;
- value types passed through object-based `Set`/`Where` APIs;
- user listeners, equality/hash implementations, and event preparation.

For a zero-allocation runtime path:

- create and reuse concrete event instances;
- store changing payload in strongly typed fields;
- use query routing only where it materially reduces delivery;
- use a bounded set of stable reference tokens or cached boxed route values;
- create all local roots, queries, listeners, handles, and route branches before
  interactive runtime;
- allow unobserved event types to pass through local buses without creating empty
  roots on buses that have no listeners for those types;
- prewarm the actual concrete publication and listener path;
- prohibit runtime query/subscription mutation;
- verify the warmed path in the Unity Profiler and on the target device.

The allocation test in `Tests/DeterministicDispatchTests.cs` must not be generalized
beyond its prepared route.

## Threading contract

The bus is single-thread owned and not thread-safe. Only the process-wide ID increment
uses `Interlocked`. Query dictionaries, listener arrays, pending buffers, dispatch
depths, event route dictionaries, and propagation state are unsynchronized.

In Unity, construct, subscribe, unsubscribe, filter, and raise on the main thread unless
an external owner serializes every access. Never publish the same event instance
concurrently.

## Attention checklist

Before shipping an EventBusLib path, verify all of the following:

- The event type's scope is deliberately global or owned by one local bus.
- Publication uses the exact intended concrete event type.
- Ordinary payload uses typed event fields; query routes are used only for routing.
- Each route type uses exactly one family: reference-type `IParameter` marker or plain
  class route.
- Struct `IParameter` routes, null route values, mutable equality keys, and fluent
  `Set` publication are not used.
- Every reused event overwrites every route that can change.
- Route values and query topology are bounded and constructed before runtime.
- Every listener handle has one lifetime owner, is unsubscribed once, and is discarded.
- No listener mutates queries/subscriptions or re-raises the same event instance.
- Listener and equality/hash code cannot throw during normal dispatch.
- No consumer writes `EventBus<T>.SelfQuery` or directly constructs parameter-query
  implementation types.
- All access is serialized to one thread.
- Allocation claims refer only to the concrete warmed path actually measured.
- Relevant open items in [KNOWN_ISSUES.md](KNOWN_ISSUES.md) have been checked against
  the consumer's use case.

## Test coverage map

The repository tests provide evidence for:

- static and local basic publication;
- local delivery isolation;
- unobserved local publication without query-topology allocation;
- marker and class query matching on covered inputs;
- chained filters on covered paths;
- direct listener registration order and per-query duplicate suppression;
- unsubscribe/resubscribe ordering;
- first-definition marker branch order;
- same-query deferred subscription during dispatch;
- recovery of covered pending state after a listener exception;
- per-top-level-raise propagation reset and new ID assignment;
- ID preservation through covered query chains;
- different IDs for a nested different event instance;
- one fully prepared warmed `Raise` path with zero measured managed allocation.

The suite does not cover every null, reentrant same-instance, overload ambiguity,
listener-generation, concurrency, lifetime, or allocation case. Consult
[KNOWN_ISSUES.md](KNOWN_ISSUES.md) before broadening guarantees.
