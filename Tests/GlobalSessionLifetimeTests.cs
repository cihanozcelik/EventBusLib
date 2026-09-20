using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace Nopnag.EventBusLib.Tests
{
  public sealed class GlobalSessionLifetimeTests
  {
    sealed class First : BusEvent { }
    sealed class Second : BusEvent { }
    sealed class Route { }
    sealed class Marker : IParameter { }
    sealed class Receiver { public void Receive(First e) { } }
    sealed class ResetFailure : BusEvent
    {
      public override void ResetPropagation() => throw new InvalidOperationException("reset failed");
    }

    [SetUp] public void Begin() => EventBus.ClearAll();
    [TearDown] public void End() => EventBus.ClearAll();

    [Test]
    public void ClearPreservesRootsAndRemovesEveryGlobalTypeAndFilter()
    {
      var first = EventBus.Query<First>();
      var second = EventBus.Query<Second>();
      var route = new Route();
      var key = new object();
      var calls = 0;
      first.Listen(_ => calls++);
      second.Listen(_ => calls++);
      var classLeaf = first.Where(route);
      var markerLeaf = classLeaf.Where<Marker>(key);
      classLeaf.Listen(_ => calls++);
      markerLeaf.Listen(_ => calls++);
      var e = new First();
      e.Set(route);
      e.Set<Marker>(key);
      EventBus.Raise(e);
      EventBus.Raise(new Second());
      Assert.That(calls, Is.EqualTo(4));

      EventBus.ClearAll();
      Assert.That(EventBus.Query<First>(), Is.SameAs(first));
      Assert.That(EventBus.Query<Second>(), Is.SameAs(second));
      EventBus.Raise(e);
      EventBus.Raise(new Second());
      Assert.That(calls, Is.EqualTo(4));
      Assert.Throws<ObjectDisposedException>(() => classLeaf.Listen(_ => calls++));
      Assert.Throws<ObjectDisposedException>(() => markerLeaf.Raise(e));
      Assert.Throws<ObjectDisposedException>(() => classLeaf.Where<Marker>(key));
      Assert.That(first.Where(route), Is.Not.SameAs(classLeaf));
    }

    [Test]
    public void UnsubscribeRetainsFilterKeyUntilClearEvenWhenLeafIsRetained()
    {
      var key = CreateUnsubscribedFilter(out var leaf);
      Collect();
      Assert.That(key.IsAlive, Is.True, "Unsubscribe alone does not remove the routing key.");
      EventBus.ClearAll();
      Collect();
      Assert.That(key.IsAlive, Is.False);
      Assert.Throws<ObjectDisposedException>(() => leaf.Raise(new First()));
      GC.KeepAlive(leaf);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CreateUnsubscribedFilter(out EventQuery<First> leaf)
    {
      var route = new Route();
      leaf = EventBus<First>.Where(route);
      leaf.Listen(_ => { }).Unsubscribe();
      return new WeakReference(route);
    }

    [Test]
    public void ClearReleasesListenerTargetEvenWhenHandleIsRetained()
    {
      var target = CreateReceiver(out var handle);
      EventBus.ClearAll();
      Collect();
      Assert.That(target.IsAlive, Is.False);
      Assert.DoesNotThrow(handle.Unsubscribe);
      GC.KeepAlive(handle);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CreateReceiver(out IIListener handle)
    {
      var receiver = new Receiver();
      handle = EventBus<First>.Listen(receiver.Receive);
      return new WeakReference(receiver);
    }

    static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

    [Test]
    public void OldHandlesCannotRemoveNewRegistrationsWithOrWithoutClear()
    {
      var calls = 0;
      ListenerDelegate<First> callback = _ => calls++;
      var old = EventBus<First>.Listen(callback);
      var duplicate = EventBus<First>.Listen(callback);
      old.Unsubscribe();
      var current = EventBus<First>.Listen(callback);
      duplicate.Unsubscribe();
      EventBus.Raise(new First());
      Assert.That(calls, Is.EqualTo(1));
      EventBus.ClearAll();
      EventBus<First>.Listen(callback);
      current.Unsubscribe();
      EventBus.Raise(new First());
      Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void DeferredUnsubscribeAndResubscribeHaveDifferentIdentities()
    {
      var query = EventBus.Query<First>();
      var calls = 0;
      var changed = false;
      ListenerDelegate<First> callback = _ => calls++;
      IIListener old = null;
      query.Listen(_ =>
      {
        if (changed) return;
        changed = true;
        old.Unsubscribe();
        query.Listen(callback);
        old.Unsubscribe();
      });
      old = query.Listen(callback);
      query.Raise(new First());
      query.Raise(new First());
      Assert.That(calls, Is.EqualTo(2), "Current dispatch stays stable; only the new registration survives.");
    }

    [Test]
    public void ClearDuringNestedOrLocalDispatchRejectsBeforeAnyMutation()
    {
      var local = new LocalEventBus();
      var calls = 0;
      EventBus<Second>.Listen(_ => calls++);
      local.On<First>().Listen(_ =>
      {
        Assert.Throws<InvalidOperationException>(EventBus.ClearAll);
        EventBus.Raise(new Second());
      });
      EventBus<First>.Listen(e => local.Raise(e));
      EventBus.Raise(new First());
      local.Raise(new First());
      Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void ClearPreservesLocalBusAndMonotonicIdsAcrossFiveSessions()
    {
      var local = new LocalEventBus();
      var localCalls = 0;
      local.On<First>().Listen(_ => localCalls++);
      var e = new First();
      long previous = 0;
      for (var session = 0; session < 5; session++)
      {
        EventBus.ClearAll();
        var calls = 0;
        EventBus<First>.Listen(_ => calls++);
        EventBus.Raise(e);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(e.RaiseUniqueId, Is.GreaterThan(previous));
        previous = e.RaiseUniqueId;
        local.Raise(e);
        Assert.That(e.RaiseUniqueId, Is.GreaterThan(previous));
        previous = e.RaiseUniqueId;
      }
      Assert.That(localCalls, Is.EqualTo(5));
    }

    [Test]
    public void DispatchFailureDoesNotPermanentlyBlockCleanup()
    {
      EventBus<First>.Listen(_ => throw new InvalidOperationException("listener failed"));
      Assert.Throws<InvalidOperationException>(() => EventBus.Raise(new First()));
      Assert.DoesNotThrow(EventBus.ClearAll);
      var e = new ResetFailure();
      Assert.Throws<InvalidOperationException>(() => EventBus.Raise(e));
      Assert.Throws<InvalidOperationException>(() => EventBus.Raise(e));
      Assert.DoesNotThrow(EventBus.ClearAll);
    }
  }
}
