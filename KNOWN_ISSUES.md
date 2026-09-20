> Session cleanup update (pending Unity verification): ClearAll, readonly global
> roots and registration-identity handles supersede EB-007 and the global-bus parts
> of EB-014/EB-016 below. ResetPropagation exception unwinding also addresses EB-006.
> Historical descriptions below document the pinned baseline, not the new API.
> GlobalSessionLifetimeTests covers the new contracts; Unity results are pending.

# EventBusLib Known Issues

This document records implementation defects, unsafe public escape hatches, lifecycle
surprises, and test gaps found during a source audit of revision `12822a0`.

It is deliberately separate from the supported API contract:

- An item here is not automatically a release blocker for every consumer.
- Consumers should use the safe-usage guidance unless their use case reaches the item.
- When an item becomes relevant, fix it in this library with a focused regression test
  instead of adding an application-level workaround.
- Source evidence confirms the described implementation path. Where no regression test
  exists, the item is explicitly marked as a test gap rather than a tested guarantee.

## Routing correctness defects

### EB-001 — An absent struct `IParameter` can match its default-value route

**Severity:** Critical  
**Status:** Open correctness and allocation defect; missing regression test

`BusEvent.Get<T>()` returns `default(T)` as `object` when a route parameter is absent.
For a struct `IParameter`, this boxes a non-null default value. `ParameterQuery` checks
only `value != null` before dictionary lookup, so an event on which the parameter was
never set can match `Where<Route>(default(Route))`.

The missing-route lookup can also allocate because boxing occurs during dispatch.

**Safe usage:** Use reference-type `IParameter` route markers. Do not use struct route
types until missing-value representation is fixed and tested.

**Evidence:**

- `Runtime/BusEvent.cs:31-37`
- `Runtime/EventBus.cs:321-325`

**Missing coverage:** Absent, explicitly set default, and non-default struct routes,
including warmed allocation measurements.

### EB-002 — `class + IParameter` types can silently select different route stores

**Severity:** Critical  
**Status:** Open overload-resolution defect; missing cross-combination tests

The library has two independent parameter systems:

- `Set<T>(T) where T : class` and class `Where<T>(T)` use the generic/class store.
- `Set<T>(object) where T : IParameter` and marker `Where<T>(object)` use the
  `IParameter` store.

When a route type is both a class and `IParameter`, C# overload resolution depends on
the argument's static type. `Set` can therefore write one store while `Where` listens
to the other, producing a silent non-match for the same apparent type and value.

**Safe usage:** Do not make one route type participate in both parameter systems. For
the marker API, pass a consistently typed `object` and retrieve with `Get<T>()`. For
the class API, do not implement `IParameter` and use `GetGeneric<T>()` consistently.

**Evidence:**

- `Runtime/BusEvent.cs:31-45,57-74`
- `Runtime/EventBus.cs:78-88,227-265,299-388`

**Missing coverage:** All `Set`/`Where`/`Get` overload combinations for a class that
also implements `IParameter`.

### EB-003 — Fluent `Set` can erase the derived event type used for routing

**Severity:** High  
**Status:** Open API design trap

Both `Set` overloads return `BusEvent`, not the concrete event type. Event routing is
exactly keyed by the generic event type. Consequently, a fluent expression such as
`EventBus.Raise(new DerivedEvent().Set<Route>(value))` can infer `BusEvent` and publish
to the `EventBus<BusEvent>` root instead of `EventBus<DerivedEvent>`.

There is no polymorphic delivery from a derived event root to base-event listeners or
the reverse.

**Safe usage:** Store the concrete event in a concrete-typed variable, call `Set` as a
separate statement, and raise using the concrete type. Do not depend on inheritance
for event delivery.

**Evidence:**

- `Runtime/BusEvent.cs:57-75`
- `Runtime/EventBus.cs:39-50,54-75,101-117`

**Missing coverage:** Derived/base event routing and fluent `Set` type inference.

### EB-004 — Filter equality is value equality, not guaranteed identity

**Severity:** Medium  
**Status:** Current behavior; incompletely documented and tested

Filter values are keys in `Dictionary<object, EventQuery<T>>` and therefore use the
key object's default `Equals` and `GetHashCode` behavior. Reference types that override
equality use value semantics; reference identity is not guaranteed. Mutating fields
that participate in equality or hashing after a key is registered can make its branch
unreachable or produce inconsistent routing.

**Safe usage:** Use immutable, stable route tokens with stable equality and hash codes.
Use explicit identity tokens when identity routing is required.

**Evidence:** `Runtime/EventBus.cs:302-342,348-388`

**Missing coverage:** Reference identity versus value equality, mutable keys, hash
collisions, and equality/hash exceptions.

### EB-005 — Null cannot be used as a filtered route value

**Severity:** Medium  
**Status:** Current inconsistent null behavior

`Set` permits storing null. Filter dispatch skips null values, so explicitly null and
absent values are indistinguishable. `Where(..., null)` attempts to use a null
dictionary key and throws `ArgumentNullException`.

**Safe usage:** Never use null as a routing value. Represent the route with an explicit
non-null sentinel token if needed.

**Evidence:**

- `Runtime/BusEvent.cs:57-74`
- `Runtime/EventBus.cs:321-325,333-341,367-371,379-387`

**Missing coverage:** Null class and marker routes and an explicit absent/null policy.

## Dispatch, propagation, and listener defects

### EB-006 — A throwing `ResetPropagation` override can permanently poison an event

**Status:** Fixed in the session-cleanup implementation; Unity verification pending.
EventDispatch now unwinds the event depth and active-dispatch counter if the override
throws. GlobalSessionLifetimeTests.DispatchFailureDoesNotPermanentlyBlockCleanup
covers repeated failure and subsequent cleanup. Exceptions still propagate; callbacks
must not treat this as recovery of application state.

### EB-007 — Listener handles are delegate-based, not subscription-generation-based

**Status:** Fixed for handles returned by Listen; Unity verification pending.
A registration object is its identity. Duplicate Listen shares that object; a new
registration gets a new object. Unsubscribe requests are idempotent, including while
pending. Removal/clear detaches the handle from both callback and query. Tests cover
stale handles with/without ClearAll, duplicate handles, and deferred resubscription.
Manually constructing the legacy Listener struct is outside these guarantees.

### EB-008 — Same-event reentrant raises share propagation and ID across buses

**Severity:** High  
**Status:** Current behavior with dangerous reentrancy implications

The definition of a top-level raise is stored on the event instance through
`ActiveRaiseDepth`; it is not scoped to a particular global or local bus. If a listener
synchronously raises the same event instance on another query or bus, the nested raise
keeps the existing `RaiseUniqueId` and propagation state. A nested
`StopPropagation()` can affect the outer traversal.

Re-raising the same instance recursively on the same query without an external guard
can recurse until stack overflow.

Local buses are isolated in listener topology, but they are not isolated from this
event-instance dispatch state or from the global ID counter.

**Safe usage:** Never synchronously re-raise the same event instance. Use a distinct,
preowned event instance for a nested publication and keep event objects single-owner
during dispatch.

**Evidence:**

- `Runtime/BusEvent.cs:23-29`
- `Runtime/EventBus.cs:30-37,184-224,309-376`
- `Tests/DeterministicDispatchTests.cs:163-188`

**Missing coverage:** Same-instance nested raises across same query, different event
queries, local-to-local, and local/global combinations, including propagation stop.

### EB-009 — Listener exceptions abort the remaining dispatch

**Severity:** Medium  
**Status:** Current behavior; cleanup is partial but tested only narrowly

A listener exception propagates synchronously to the caller. Remaining listeners and
filter branches are not invoked. `finally` restores dispatch depths and applies pending
operations for queries whose stack frames are unwound, so those query objects normally
remain usable afterward.

This is not an aggregate-or-continue event bus. Cleanup failures or virtual reset
failures can still violate the recovery behavior described in other issues.

**Safe usage:** Listener code must not throw during normal dispatch. The publication
owner must define failure handling; do not assume later listeners ran.

**Evidence:**

- `Runtime/EventBus.cs:194-224,319-330,365-376`
- `Tests/DeterministicDispatchTests.cs:141-160`

**Missing coverage:** Later-listener suppression, exceptions in child queries,
deferred unsubscribe after exceptions, and exception masking during cleanup.

### EB-010 — Null events and listeners fail late

**Severity:** Medium  
**Status:** Open validation gap

Raise methods do not validate a null event before accessing its dispatch state and
therefore throw `NullReferenceException`. `Listen(null)` can insert a null delegate;
dispatch later attempts to invoke it and throws. The failure does not occur at the
registration boundary where the setup error was made.

**Safe usage:** Events and listeners are required non-null inputs. Validate them at the
consumer's setup boundary until the library adds explicit argument checks.

**Evidence:**

- `Runtime/EventBus.cs:48-50,63-75,101-117,168-197`
- `Runtime/OrderedListenerSet.cs:37-65,111-120,156-159`

**Missing coverage:** Null event and listener failure contracts for every public raise
and listen entry point.

## Query topology and public API hazards

### EB-011 — Mutation deferral is per query, not per overall event dispatch

**Severity:** High  
**Status:** Current subtle behavior; incompletely tested

Listen and unsubscribe operations are deferred only when the target `EventQuery` is
currently dispatching. A root listener can mutate a different filtered query that has
not started dispatching yet; that mutation applies immediately and may affect the same
overall event raise.

`Where` topology construction is never deferred. Phase-dependent results include:

- A new first-level marker branch added by a root listener can be included in the same
  raise because the branch count is captured after root listeners.
- A marker branch added while marker branches are already being iterated is deferred
  only accidentally by the captured count.
- A class branch added during the marker phase can be included because the class count
  is captured later.

**Safe usage:** Construct all queries and listeners before runtime publication. Do not
call `Listen`, `Unsubscribe`, or `Where` from listeners when deterministic topology is
required.

**Evidence:** `Runtime/EventBus.cs:168-175,194-217,227-296`

**Missing coverage:** Cross-query subscription/unsubscription and `Where` mutation in
each dispatch phase.

### EB-012 — Public mutable `EventBus<T>.SelfQuery` can break the global bus

**Severity:** High  
**Status:** Open public-surface hazard

`SelfQuery` is a public mutable static field. Replacing it disconnects existing
listeners from future publications. Assigning null has inconsistent behavior:
`EventBus<T>.Raise` recreates the root, while `Listen`, `Where`, and
`EventBus.Query<T>()` can dereference or return null.

**Safe usage:** Treat `SelfQuery` as internal read-only state. Never assign or replace
it in consumer code or tests.

**Evidence:** `Runtime/EventBus.cs:39-42,54-88`

**Missing coverage:** Null and replacement behavior through every global entry point.

### EB-013 — Public parameter-query types are unsafe to construct and raise directly

**Severity:** High  
**Status:** Open public-surface design defect

`ParameterQuery<T,TParameter>` and `GenericParameterQuery<T,TParameter>` are public.
Their `Raise` overrides route only to a value child and do not invoke listeners stored
on the parameter-query object itself. A consumer can publicly construct one, call its
inherited `Listen`, then raise it and observe that the listener never fires.

Raising a leaf `EventQuery` directly also bypasses ancestor filter conditions. Direct
query raise is therefore not equivalent to publishing through the owning root.

**Safe usage:** Obtain queries only through `EventBus<T>.Where(...)` or
`LocalEventBus.On<T>().Where(...)`, retain the returned leaf only for `Listen`, and
publish through `EventBus.Raise`, `EventBus<T>.Raise`, or the owning local bus.

**Evidence:** `Runtime/EventBus.cs:121-265,299-389`

**Missing coverage:** Public construction, inherited listeners on parameter-query
objects, and direct root/branch/leaf raise behavior.

### EB-014 — Route values and query branches have no removal or clear API

**Severity:** High  
**Status:** Current lifetime limitation

`BusEvent` parameter dictionaries persist for the event object's lifetime. Reusing an
event without overwriting every relevant route can publish stale route values. There
is no `Remove` or `ClearParameters`; `ResetPropagation` resets only the stop flag.

Query filter dictionaries also retain route keys and empty branch objects after all
listeners unsubscribe. Global roots retain them until EventBus.ClearAll; that lifecycle operation invalidates
old filter queries and releases all route keys.
Long-lived local buses retain them for the local bus lifetime.

**Safe usage:** Define each reusable event's complete route state at its initialization
or publication boundary. Use a bounded, stable route-key set. Do not create unbounded
runtime route values or assume unsubscribe removes query topology.

**Evidence:**

- `Runtime/BusEvent.cs:12-19,31-75`
- `Runtime/EventBus.cs:143-160,227-265,299-388`

**Missing coverage:** Stale route reuse, key retention after unsubscribe, and bounded
topology/lifetime behavior.

### EB-015 — Filter-chain ordering is structural and can duplicate equivalent paths

**Severity:** Medium  
**Status:** Current behavior; partial test coverage

Chained `Where` calls have AND semantics along a query-tree path. `A -> B` and
`B -> A` create separate branches. Both can match one event and invoke the same
delegate twice because delegate de-duplication is local to each individual query, not
global across the event dispatch.

At every level, direct listeners run first, then `IParameter` type branches in their
first-definition order, followed by class-parameter branches in their own
first-definition order.

**Safe usage:** Choose one canonical filter-chain order for each event and route set.
Do not register the same logical listener through equivalent permutations.

**Evidence:**

- `Runtime/EventBus.cs:194-217,227-265`
- `Runtime/OrderedListenerSet.cs:111-123`
- `Tests/DeterministicDispatchTests.cs:21-138`

**Missing coverage:** Equivalent chain permutations, mixed marker/class ordering, and
deep nested ordering.

## Lifetime and concurrency limits

### EB-016 — Static subscriptions and route keys can retain scene/run objects

**Severity:** High  
**Status:** Current lifetime limitation

Every closed `EventBus<T>` owns a readonly static root query. EventBus.ClearAll
clears initialized global roots in place; Unity hooks call it at Play startup and
after Play teardown. Owner-driven unsubscribe is still required during a session. Listener delegates, unsubscribe closures,
and route-key dictionaries hold strong references. Missing unsubscription can retain
scene, map, run, or test objects after their intended lifetime.

A `LocalEventBus` can be collected when the bus and all handles become unreachable,
but a live external listener handle retains its query and callback until unsubscribed
or globally cleared. Returned handles release both references on removal. The bus itself
has no `Dispose` or bulk-clear API.

**Safe usage:** Assign every subscription handle to an explicit lifetime owner and
unsubscribe before that owner ends. Use local buses for bounded scopes and discard the
entire bus plus handles together.

**Evidence:** `Runtime/EventBus.cs:54-60,92-119,143-175,227-265`

**Missing coverage:** Static subscriber reachability, local bus/handle reachability,
and scene/domain lifecycle cleanup.

### EB-017 — The event bus is not thread-safe

**Severity:** High  
**Status:** Current unsupported usage

Only the global raise ID counter uses `Interlocked`. Listener collections, query
dictionaries, pending-operation buffers, dispatch depths, event route dictionaries,
and event propagation state are unsynchronized. Concurrent raises on the same query
or event instance and concurrent topology mutation can race or corrupt state.

**Safe usage:** Construct, subscribe, unsubscribe, filter, and raise from one owning
thread, normally Unity's main thread. Never concurrently publish the same event
instance.

**Evidence:**

- `Runtime/BusEvent.cs:12-29`
- `Runtime/EventBus.cs:30-37,94-117,143-160,168-296`
- `Runtime/OrderedListenerSet.cs:24-154`

**Missing coverage:** No concurrency behavior is tested or promised.

### EB-018 — Raise ID overflow is not defined

**Severity:** Low  
**Status:** Theoretical long-process limitation

The global signed `long` counter increments without overflow handling. After wrapping,
IDs can become non-positive and eventually repeat. Current tests assume newly assigned
IDs are positive.

**Safe usage:** Do not use `RaiseUniqueId` as a persistent globally unique identifier;
it is only a transient dispatch discriminator.

**Evidence:**

- `Runtime/EventBus.cs:32-37`
- `Tests/EventBusTest.cs:598-689`

## Allocation and test-coverage limits

### EB-019 — The zero-allocation test covers only a fully warmed fixed topology

**Severity:** High  
**Status:** Test-claim limitation

The existing deterministic-dispatch allocation test prepares a class-marker query,
listener, and reused event, warms the path, then measures repeated `Raise`. A separate
local-bus regression test covers the first unobserved raise without query creation.
Neither test proves allocation freedom for event construction or preparation, first
static use, `LocalEventBus.On<T>`, listener registration, query creation, subscription
mutation, value-type routes, virtual user code, or container growth.

Known allocation points include:

- `BusEvent` construction allocates two dictionaries.
- `EventQuery` construction allocates dictionaries, lists, listener storage, and a
  pending-operation array.
- First `LocalEventBus.On<T>()` for an event type creates a query. An unobserved local
  raise does not create query topology.
- Every `Listen` creates an unsubscribe closure and returns a struct through
  `IIListener`, which can box.
- Listener storage, query dictionaries, and pending-operation arrays resize.
- Value-type arguments passed through object route APIs box.
- An absent struct route can box during dispatch as described by EB-001.

**Safe usage:** Build and warm the complete topology for observed event types before
interactive runtime, reuse event instances, use stable reference route tokens, and
confirm the real path with the Unity Profiler. Buses that do not observe an event type
do not need an empty root solely to let that event pass through scoped propagation. Do
not claim that arbitrary `Raise` usage is allocation free.

**Evidence:**

- `Runtime/BusEvent.cs:12-19,63-74`
- `Runtime/EventBus.cs:54-88,128-160,164-262,336-402`
- `Runtime/OrderedListenerSet.cs:31-35,125-153`
- `Tests/DeterministicDispatchTests.cs:191-212`
- `Tests/EventBusInstanceTest.cs:184-221`

### EB-020 — Runtime pending-operation growth can allocate during dispatch

**Severity:** Medium  
**Status:** Current allocation hazard

Subscribe or unsubscribe requests against the same actively dispatching query are
stored in an array with initial capacity four. A fifth pending mutation resizes the
array during the raise. Even below that threshold, calling `Listen` constructs a new
handle/closure.

**Safe usage:** Do not mutate subscriptions from runtime listeners. Own subscription
changes at initialization or explicit non-interactive lifecycle boundaries.

**Evidence:** `Runtime/EventBus.cs:123-160,168-175,267-296`

**Missing coverage:** Allocation measurement for listener registration and pending
mutation overflow.

### EB-021 — Existing tests do not consistently clean up static subscriptions

**Severity:** Medium  
**Status:** Test isolation weakness

Several tests register global listeners without retaining or unsubscribing their
handles. Static roots can therefore accumulate listeners and route topology for the
test domain. Many tests use distinct event types, which reduces visible interference,
but the suite does not establish strict lifetime isolation.

**Safe usage:** Every test must own and unsubscribe every global listener in teardown,
or use a unique event type and an explicit test-only reset policy added to the library.

**Evidence:** Examples occur throughout `Tests/EventBusTest.cs`; static root lifetime
is defined in `Runtime/EventBus.cs:54-60`.

**Missing coverage:** Test-domain cleanup and strong-reference reachability.

## Current tested guarantees must remain narrowly stated

The existing suite provides useful evidence for these paths:

- Direct listeners run in registration order.
- Duplicate delegates on one query are set-like.
- Unsubscribe then resubscribe moves a listener to the end.
- Same-query mutation during dispatch is deferred.
- Marker filter-type branches use first-definition order.
- Listener exceptions do not leave the tested query depth/pending subscribe state
  poisoned.
- Separate top-level raises assign new IDs and reset propagation.
- Global and local listener topologies are delivery-isolated.
- A fully prepared, warmed, reused-event raise path can perform zero measured managed
  allocation in the covered test.

These tests do not close the known issues above and must not be generalized to null,
concurrent, reentrant same-instance, dynamically mutating, or unprepared paths.
