using NUnit.Framework;

namespace Nopnag.EventBusLib.Tests
{
  public class EventBusTest
  {
    public class Amount : object, IParameter
    {
    }

    // New Event Type for re-entrancy test
    public class AnotherTestEvent : BusEvent
    {
    }

    public class Destination : Warrior, IParameter
    {
    }

    // New Event Type for DoNotClearOnClearAllTest
    public class DifferentEventForClearAll : BusEvent
    {
    }

    public class OnHitEvent : BusEvent
    {
    }

    public class Source : Warrior, IParameter
    {
    }

    public class Warrior
    {
    }

    public class WeaponType : object, IParameter
    {
    }

    public enum Weapons
    {
      Kilic,
      Yildiz,
      UcanKafa,
      Tokat
    }

    Warrior warrior1;
    Warrior warrior2;
    Warrior warrior3;
    Warrior warrior4;

    [Test]
    public void BasicRaise()
    {
      var called = false;
      EventBus<OnHitEvent>.Listen(e => { called = true; });

      // Create event
      OnHitEvent slapEvent;
      slapEvent = new OnHitEvent();
      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.IsTrue(called); // Assert that the listener was called
    }

    [Test]
    public void EventBusTestSimplePasses()
    {
      var calledCorrectly = false;
      // Add listener 
      EventBus<OnHitEvent>.Where<Source>(warrior1).Where<Destination>(warrior2)
        .Where<WeaponType>(Weapons.Tokat).Listen(e =>
        {
          // This listener should NOT be called because the WeaponType is wrong
          Assert.Fail("Tokat listener should not be called for Kilic event.");
        });

      EventBus<OnHitEvent>.Where<Source>(warrior1).Where<Destination>(warrior2)
        .Where<WeaponType>(Weapons.Kilic).Listen(e =>
        {
          calledCorrectly = true; // This listener SHOULD be called
        });

      // Create event
      var slapEvent = new OnHitEvent();
      slapEvent.Set<Source>(warrior1).Set<Destination>(warrior2).Set<WeaponType>(Weapons.Kilic);

      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.IsTrue(calledCorrectly,
        "Kilic listener was not called."); // Check if the correct listener was called
    }

    [Test]
    public void ListenerCanRaiseAnotherEventSafely() // Renamed for clarity
    {
      var onHitListenerCalled        = false;
      var anotherEventListenerCalled = false;
      var onHitCallCount             = 0;

      var onHitSubscription = EventBus<OnHitEvent>.Listen(e =>
      {
        // Prevent re-entrancy for this specific listener if the event somehow looped
        if (onHitCallCount > 0) return;
        onHitCallCount++;

        onHitListenerCalled = true;
        EventBus<AnotherTestEvent>.Raise(new AnotherTestEvent());
      });

      var anotherSubscription =
        EventBus<AnotherTestEvent>.Listen(e => { anotherEventListenerCalled = true; });

      EventBus<OnHitEvent>.Raise(new OnHitEvent());

      Assert.IsTrue(onHitListenerCalled, "OnHitEvent listener should have been called.");
      Assert.IsTrue(anotherEventListenerCalled,
        "AnotherTestEvent listener should have been called due to raise from OnHitEvent listener.");

      // Cleanup
      onHitSubscription?.Unsubscribe();
      anotherSubscription?.Unsubscribe();
    }

    [Test]
    public void ListenerCanSubscribeAnotherListenerDuringRaise()
    {
      var        listener1CallCount = 0;
      var        listener2CallCount = 0;
      IIListener listener1Handle    = null;
      IIListener listener2Handle    = null;

      listener1Handle = EventBus<OnHitEvent>.Listen(e =>
      {
        listener1CallCount++;
        if (listener1CallCount == 1) // Only subscribe Listener2 on the first call of Listener1
          listener2Handle = EventBus<OnHitEvent>.Listen(e2 => { listener2CallCount++; });
      });

      var anEvent = new OnHitEvent();
      EventBus<OnHitEvent>.Raise(anEvent);
      Assert.AreEqual(1, listener1CallCount, "Listener1 called once for first raise.");
      Assert.AreEqual(0, listener2CallCount,
        "Listener2 should not be called during the same raise it was subscribed in.");

      EventBus<OnHitEvent>.Raise(anEvent);
      Assert.AreEqual(2, listener1CallCount, "Listener1 called again for second raise.");
      Assert.AreEqual(1, listener2CallCount,
        "Listener2 should now be called for the second raise.");

      // Cleanup
      listener1Handle?.Unsubscribe();
      listener2Handle?.Unsubscribe();
    }

    [Test]
    public void ListenerCanUnsubscribeItself()
    {
      IIListener listenerHandle = null;
      var        callCount      = 0;
      // Assign to local variable before lambda capture for safety with some C# versions/compiler behaviors
      IIListener tempListenerHandle = null;
      tempListenerHandle = EventBus<OnHitEvent>.Listen(e =>
      {
        callCount++;
        tempListenerHandle?.Unsubscribe(); // Use the captured local variable
      });
      listenerHandle =
        tempListenerHandle; // Assign to outer scope variable (though not strictly needed for this test logic)

      var anEvent = new OnHitEvent();
      EventBus<OnHitEvent>.Raise(anEvent); // First raise, listener runs and unsubscribes
      EventBus<OnHitEvent>.Raise(anEvent); // Second raise, listener should not run

      Assert.AreEqual(1, callCount, "Listener should have been called only once.");
    }


    [Test]
    public void MultiParameterWithGettingParamatersFromEventTest()
    {
      var warrior1EventCalled = false;


      EventBus<OnHitEvent>.Where<Source>(warrior1).Listen(
        e =>
        {
          // Check if all parameters match the raised event
          Assert.AreEqual(e.Get<Source>(), warrior1);
          Assert.AreEqual(e.Get<Destination>(), warrior2);
          Assert.AreEqual(e.Get<Amount>(), 15);
          Assert.AreEqual(e.Get<WeaponType>(), Weapons.Tokat);
          warrior1EventCalled = true;
        }
      );


      // Create event
      var slapEvent = new OnHitEvent();
      slapEvent.Set<Source>(warrior1);
      slapEvent.Set<Destination>(warrior2);
      slapEvent.Set<Amount>(15);
      slapEvent.Set<WeaponType>(Weapons.Tokat);

      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.True(warrior1EventCalled);
    }

    [Test]
    public void MultipleSubscribersSimpleEvent()
    {
      var listener1 = false;
      var listener2 = false;
      var listener3 = false;

      EventBus<OnHitEvent>.Listen(e => { listener1 = true; });

      EventBus<OnHitEvent>.Listen(e => { listener2 = true; });

      EventBus<OnHitEvent>.Listen(e => { listener3 = true; });

      // Create event
      OnHitEvent slapEvent;
      slapEvent = new OnHitEvent();
      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);

      Assert.True(listener1);
      Assert.True(listener2);
      Assert.True(listener3);
    }

    [SetUp]
    public void Setup()
    {
      warrior1 = new Warrior();
      warrior2 = new Warrior();
      warrior3 = new Warrior();
      warrior4 = new Warrior();
    }


    [Test]
    public void SingeSubscribersMultipleEvents()
    {
      var signalCount = 0;
      EventBus<OnHitEvent>.Listen(e => { signalCount++; });

      // Create event
      var slapEvent  = new OnHitEvent();
      var slapEvent2 = new OnHitEvent();

      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);
      EventBus<OnHitEvent>.Raise(slapEvent2);

      Assert.AreEqual(signalCount, 2);
    }

    [Test]
    public void SingleParameterTest()
    {
      var warrior1EventCalled    = false;
      var noParameterEventCalled = false; // Listener without parameter filter

      EventBus<OnHitEvent>.Where<Source>(warrior1).Listen(e => { warrior1EventCalled = true; });

      // This listener should always be called when OnHitEvent is raised
      EventBus<OnHitEvent>.Listen(e => { noParameterEventCalled = true; });

      EventBus<OnHitEvent>.Where<Source>(warrior2).Listen(e =>
      {
        Assert.Fail("Warrior2 listener should not be called.");
      });


      // Create event with Source = warrior1
      var slapEvent = new OnHitEvent();
      slapEvent.Set<Source>(warrior1);

      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);

      Assert.True(warrior1EventCalled, "Warrior1 specific listener was not called.");
      Assert.True(noParameterEventCalled, "Generic listener was not called.");
    }

    // --- END OF NEW TESTS ---

    [Test]
    public void StopPropagationAndResetPropagationTest()
    {
      var eventInstance              = new OnHitEvent();
      var listener1CalledFirstRaise  = false;
      var listener2CalledFirstRaise  = false;
      var listener1CalledSecondRaise = false;
      var listener2CalledSecondRaise = false;

      var sub1 = EventBus<OnHitEvent>.Listen(e =>
      {
        if (e == eventInstance) // Ensure it's our specific event instance
        {
          // For first raise, this listener will set its flag and stop propagation
          // For second raise, it will just set its flag
          if (!listener1CalledFirstRaise)
          {
            listener1CalledFirstRaise = true;
            e.StopPropagation();
          }
          else
          {
            listener1CalledSecondRaise = true;
          }
        }
      });

      var sub2 = EventBus<OnHitEvent>.Listen(e =>
      {
        if (e == eventInstance) // Ensure it's our specific event instance
        {
          if (!listener1CalledFirstRaise) // Should only be called if listener1 wasn't (i.e. before first raise logic)
          {
            // This block should ideally not be hit if testing StopPropagation from listener1
          }
          else if (listener1CalledFirstRaise &&
                   !listener1CalledSecondRaise) // After first raise, before second raise logic
          {
            listener2CalledFirstRaise = true; // This flags an error if called on first raise
          }
          else // Second raise
          {
            listener2CalledSecondRaise = true;
          }
        }
      });

      // First Raise
      EventBus<OnHitEvent>.Raise(eventInstance);
      Assert.IsTrue(listener1CalledFirstRaise, "Listener 1 should be called on first raise.");
      Assert.IsFalse(listener2CalledFirstRaise,
        "Listener 2 should NOT be called on first raise due to StopPropagation.");

      // Reset propagation for the same event instance
      eventInstance.ResetPropagation();
      Assert.IsFalse(eventInstance.IsPropagationStopped,
        "IsPropagationStopped should be false after ResetPropagation.");


      // Second Raise (with the same instance)
      EventBus<OnHitEvent>.Raise(eventInstance);
      Assert.IsTrue(listener1CalledSecondRaise, "Listener 1 should be called on second raise.");
      Assert.IsTrue(listener2CalledSecondRaise,
        "Listener 2 should also be called on second raise after ResetPropagation.");

      // Cleanup
      sub1.Unsubscribe();
      sub2.Unsubscribe();
    }

    [Test]
    public void SubscribedMultipleParameter()
    {
      var warrior1_2EventCalled  = false; // Source=w1, Dest=w2
      var warrior1EventCalled    = false; // Source=w1
      var warrior2EventCalled    = false; // Dest=w2
      var warrior2_1EventCalled  = false; // Dest=w2, Source=w1 (same as first)
      var noParameterEventCalled = false; // No filter

      EventBus<OnHitEvent>.Where<Source>(warrior1).Where<Destination>(warrior2)
        .Listen(e => { warrior1_2EventCalled = true; });

      // Same filter, different order - should also be called
      EventBus<OnHitEvent>.Where<Destination>(warrior2).Where<Source>(warrior1)
        .Listen(e => { warrior2_1EventCalled = true; });

      EventBus<OnHitEvent>.Where<Destination>(warrior2)
        .Listen(e => { warrior2EventCalled = true; });

      EventBus<OnHitEvent>.Where<Source>(warrior1).Listen(e => { warrior1EventCalled = true; });

      EventBus<OnHitEvent>.Listen(e => { noParameterEventCalled = true; });

      EventBus<OnHitEvent>.Where<Source>(warrior2).Listen(e =>
      {
        Assert.Fail("Warrior2 only listener should not be called.");
      });

      // Create event: Source=w1, Dest=w2
      var slapEvent = new OnHitEvent();
      slapEvent.Set<Source>(warrior1);
      slapEvent.Set<Destination>(warrior2);
      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);

      Assert.True(warrior1_2EventCalled, "Listener for (Source=w1, Dest=w2) was not called.");
      Assert.True(warrior2_1EventCalled, "Listener for (Dest=w2, Source=w1) was not called.");
      Assert.True(warrior2EventCalled, "Listener for (Dest=w2) was not called.");
      Assert.True(warrior1EventCalled, "Listener for (Source=w1) was not called.");
      Assert.True(noParameterEventCalled, "Generic listener was not called.");
    }

    [Test]
    public void SubscribedMultipleParameterButRaisingWithOneParameterTest()
    {
      var warrior1_2EventCalled  = false; // Source=w1, Dest=w2
      var warrior1EventCalled    = false; // Source=w1
      var warrior2EventCalled    = false; // Dest=w2
      var warrior2_1EventCalled  = false; // Dest=w2, Source=w1
      var noParameterEventCalled = false; // No filter

      EventBus<OnHitEvent>.Where<Source>(warrior1).Where<Destination>(warrior2)
        .Listen(e => { warrior1_2EventCalled = true; });

      EventBus<OnHitEvent>.Where<Destination>(warrior2).Where<Source>(warrior1)
        .Listen(e => { warrior2_1EventCalled = true; });

      EventBus<OnHitEvent>.Where<Destination>(warrior2)
        .Listen(e => { warrior2EventCalled = true; });

      EventBus<OnHitEvent>.Where<Source>(warrior1).Listen(e => { warrior1EventCalled = true; });

      EventBus<OnHitEvent>.Listen(e => { noParameterEventCalled = true; });

      EventBus<OnHitEvent>.Where<Source>(warrior2).Listen(e =>
      {
        Assert.Fail("Warrior2 only listener should not be called.");
      });

      // Create event: Source=w1 ONLY
      var slapEvent = new OnHitEvent();
      slapEvent.Set<Source>(warrior1);
      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);

      // Only listeners matching the available parameters should be called
      Assert.False(warrior1_2EventCalled,
        "Listener for (Source=w1, Dest=w2) should NOT be called.");
      Assert.False(warrior2_1EventCalled,
        "Listener for (Dest=w2, Source=w1) should NOT be called.");
      Assert.False(warrior2EventCalled, "Listener for (Dest=w2) should NOT be called.");
      Assert.True(warrior1EventCalled, "Listener for (Source=w1) was not called.");
      Assert.True(noParameterEventCalled, "Generic listener was not called.");
    }

    [Test]
    public void TestEnum() // Placeholder - add specific enum tests if needed
    {
      var enumCalled = false;
      EventBus<OnHitEvent>.Where<WeaponType>(Weapons.Kilic).Listen(e => enumCalled = true);

      var e = new OnHitEvent();
      e.Set<WeaponType>(Weapons.Kilic);
      EventBus<OnHitEvent>.Raise(e);
      Assert.IsTrue(enumCalled);

      enumCalled = false; // Reset flag
      var e2 = new OnHitEvent();
      e2.Set<WeaponType>(Weapons.Tokat);
      EventBus<OnHitEvent>.Raise(e2);
      Assert.IsFalse(enumCalled); // Should not be called for Tokat
    }

    [Test]
    public void UnsubscribeTest()
    {
      var warrior1_2EventCalled  = false;
      var warrior1EventCalled    = false;
      var warrior2EventCalled    = false;
      var warrior2_1EventCalled  = false;
      var noParameterEventCalled = false;

      var l1 = EventBus<OnHitEvent>.Where<Source>(warrior1).Where<Destination>(warrior2)
        .Listen(e => { warrior1_2EventCalled = true; });

      var l2 = EventBus<OnHitEvent>.Where<Destination>(warrior2).Where<Source>(warrior1)
        .Listen(e => { warrior2_1EventCalled = true; });

      var l3 = EventBus<OnHitEvent>.Where<Destination>(warrior2)
        .Listen(e => { warrior2EventCalled = true; });

      var l4 = EventBus<OnHitEvent>.Where<Source>(warrior1)
        .Listen(e => { warrior1EventCalled = true; });

      var l5 = EventBus<OnHitEvent>.Listen(e => { noParameterEventCalled = true; });

      var l6 = EventBus<OnHitEvent>.Where<Source>(warrior2).Listen(e =>
      {
        Assert.Fail("Warrior 2 listener should never be called in this test");
      });

      // Create event
      var slapEvent = new OnHitEvent();
      slapEvent.Set<Source>(warrior1);
      slapEvent.Set<Destination>(warrior2);

      // Raise event - all should be called initially
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.True(warrior1_2EventCalled, "Initial Call: l1 failed");
      Assert.True(warrior2_1EventCalled, "Initial Call: l2 failed");
      Assert.True(warrior2EventCalled, "Initial Call: l3 failed");
      Assert.True(warrior1EventCalled, "Initial Call: l4 failed");
      Assert.True(noParameterEventCalled, "Initial Call: l5 failed");

      // --- Unsubscribe l1 and test ---
      warrior1_2EventCalled  = false;
      warrior2_1EventCalled  = false;
      warrior2EventCalled    = false;
      warrior1EventCalled    = false;
      noParameterEventCalled = false; // Reset flags
      l1.Unsubscribe();
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.False(warrior1_2EventCalled, "After l1 unsub: l1 called");
      Assert.True(warrior2_1EventCalled, "After l1 unsub: l2 failed");
      Assert.True(warrior2EventCalled, "After l1 unsub: l3 failed");
      Assert.True(warrior1EventCalled, "After l1 unsub: l4 failed");
      Assert.True(noParameterEventCalled, "After l1 unsub: l5 failed");

      // --- Unsubscribe l2 and test ---
      warrior1_2EventCalled  = false;
      warrior2_1EventCalled  = false;
      warrior2EventCalled    = false;
      warrior1EventCalled    = false;
      noParameterEventCalled = false; // Reset flags
      l2.Unsubscribe();
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.False(warrior1_2EventCalled, "After l2 unsub: l1 called");
      Assert.False(warrior2_1EventCalled, "After l2 unsub: l2 called");
      Assert.True(warrior2EventCalled, "After l2 unsub: l3 failed");
      Assert.True(warrior1EventCalled, "After l2 unsub: l4 failed");
      Assert.True(noParameterEventCalled, "After l2 unsub: l5 failed");

      // --- Unsubscribe l3 and test ---
      warrior1_2EventCalled  = false;
      warrior2_1EventCalled  = false;
      warrior2EventCalled    = false;
      warrior1EventCalled    = false;
      noParameterEventCalled = false; // Reset flags
      l3.Unsubscribe();
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.False(warrior1_2EventCalled, "After l3 unsub: l1 called");
      Assert.False(warrior2_1EventCalled, "After l3 unsub: l2 called");
      Assert.False(warrior2EventCalled, "After l3 unsub: l3 called");
      Assert.True(warrior1EventCalled, "After l3 unsub: l4 failed");
      Assert.True(noParameterEventCalled, "After l3 unsub: l5 failed");


      // --- Unsubscribe l4 and test ---
      warrior1_2EventCalled  = false;
      warrior2_1EventCalled  = false;
      warrior2EventCalled    = false;
      warrior1EventCalled    = false;
      noParameterEventCalled = false; // Reset flags
      l4.Unsubscribe();
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.False(warrior1_2EventCalled, "After l4 unsub: l1 called");
      Assert.False(warrior2_1EventCalled, "After l4 unsub: l2 called");
      Assert.False(warrior2EventCalled, "After l4 unsub: l3 called");
      Assert.False(warrior1EventCalled, "After l4 unsub: l4 called");
      Assert.True(noParameterEventCalled, "After l4 unsub: l5 failed");


      // --- Unsubscribe l5 and test ---
      warrior1_2EventCalled  = false;
      warrior2_1EventCalled  = false;
      warrior2EventCalled    = false;
      warrior1EventCalled    = false;
      noParameterEventCalled = false; // Reset flags
      l5.Unsubscribe();
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.False(warrior1_2EventCalled, "After l5 unsub: l1 called");
      Assert.False(warrior2_1EventCalled, "After l5 unsub: l2 called");
      Assert.False(warrior2EventCalled, "After l5 unsub: l3 called");
      Assert.False(warrior1EventCalled, "After l5 unsub: l4 called");
      Assert.False(noParameterEventCalled, "After l5 unsub: l5 called");

      // Ensure l6 is still unsubscribed (implicitly)
      // No need to explicitly check l6 as it calls Assert.Fail() if ever triggered.
    }
  }
}