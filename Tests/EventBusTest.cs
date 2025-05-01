using Nopnag.EventBus;
using NUnit.Framework;

namespace Nopnag.EventBus.Tests
{
  public class EventBusTest
  {
    public class Amount : object, IIParameter
    {
    }

    public class Destination : Warrior, IIParameter
    {
    }

    public class OnHitEvent : BusEvent
    {
    }

    public class Source : Warrior, IIParameter
    {
    }

    public class Warrior
    {
    }

    public class WeaponType : object, IIParameter
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

    [TearDown]
    public void TearDown()
    {
        // Ensure the bus is clean before each test runs
        EventBus.ClearAll?.Invoke(); 
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
    public void BasicRaise()
    {
      bool called = false;
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
      bool calledCorrectly = false;
      // Add listener 
      EventBus<OnHitEvent>.Where<Source>(warrior1).Where<Destination>(warrior2)
        .Where<WeaponType>(Weapons.Tokat).Listen(e => { 
            // This listener should NOT be called because the WeaponType is wrong
            Assert.Fail("Tokat listener should not be called for Kilic event.");
         });

       EventBus<OnHitEvent>.Where<Source>(warrior1).Where<Destination>(warrior2)
        .Where<WeaponType>(Weapons.Kilic).Listen(e => { 
            calledCorrectly = true; // This listener SHOULD be called
         });

      // Create event
      var slapEvent = new OnHitEvent(); 
      slapEvent.Set<Source>(warrior1).Set<Destination>(warrior2).Set<WeaponType>(Weapons.Kilic);

      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.IsTrue(calledCorrectly, "Kilic listener was not called."); // Check if the correct listener was called
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



    [Test]
    public void SingeSubscribersMultipleEvents()
    {
      var signalCount = 0;
      EventBus<OnHitEvent>.Listen(e => { signalCount++; });

      // Create event
      var slapEvent = new OnHitEvent();
      var slapEvent2 = new OnHitEvent();

      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);
      EventBus<OnHitEvent>.Raise(slapEvent2);

      Assert.AreEqual(signalCount, 2);
    }

    [Test]
    public void SingleParameterTest()
    {
      var warrior1EventCalled = false;
      var noParameterEventCalled = false; // Listener without parameter filter

      EventBus<OnHitEvent>.Where<Source>(warrior1).Listen(e => { warrior1EventCalled = true; });

      // This listener should always be called when OnHitEvent is raised
      EventBus<OnHitEvent>.Listen(e => { noParameterEventCalled = true; }); 

      EventBus<OnHitEvent>.Where<Source>(warrior2).Listen(e => { Assert.Fail("Warrior2 listener should not be called."); });


      // Create event with Source = warrior1
      var slapEvent = new OnHitEvent();
      slapEvent.Set<Source>(warrior1);

      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);

      Assert.True(warrior1EventCalled, "Warrior1 specific listener was not called.");
      Assert.True(noParameterEventCalled, "Generic listener was not called.");
    }

    [Test]
    public void SubscribedMultipleParameter()
    {
      var warrior1_2EventCalled = false; // Source=w1, Dest=w2
      var warrior1EventCalled = false;   // Source=w1
      var warrior2EventCalled = false;   // Dest=w2
      var warrior2_1EventCalled = false; // Dest=w2, Source=w1 (same as first)
      var noParameterEventCalled = false;// No filter

      EventBus<OnHitEvent>.Where<Source>(warrior1).Where<Destination>(warrior2)
        .Listen(e => { warrior1_2EventCalled = true; });

      // Same filter, different order - should also be called
      EventBus<OnHitEvent>.Where<Destination>(warrior2).Where<Source>(warrior1) 
        .Listen(e => { warrior2_1EventCalled = true; });

      EventBus<OnHitEvent>.Where<Destination>(warrior2).Listen(e => { warrior2EventCalled = true; });

      EventBus<OnHitEvent>.Where<Source>(warrior1).Listen(e => { warrior1EventCalled = true; });

      EventBus<OnHitEvent>.Listen(e => { noParameterEventCalled = true; });

      EventBus<OnHitEvent>.Where<Source>(warrior2).Listen(e => { Assert.Fail("Warrior2 only listener should not be called."); });

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
      var warrior1_2EventCalled = false; // Source=w1, Dest=w2
      var warrior1EventCalled = false;   // Source=w1
      var warrior2EventCalled = false;   // Dest=w2
      var warrior2_1EventCalled = false; // Dest=w2, Source=w1
      var noParameterEventCalled = false;// No filter

      EventBus<OnHitEvent>.Where<Source>(warrior1).Where<Destination>(warrior2)
        .Listen(e => { warrior1_2EventCalled = true; });

      EventBus<OnHitEvent>.Where<Destination>(warrior2).Where<Source>(warrior1)
        .Listen(e => { warrior2_1EventCalled = true; });

      EventBus<OnHitEvent>.Where<Destination>(warrior2).Listen(e => { warrior2EventCalled = true; });

      EventBus<OnHitEvent>.Where<Source>(warrior1).Listen(e => { warrior1EventCalled = true; });

      EventBus<OnHitEvent>.Listen(e => { noParameterEventCalled = true; });

      EventBus<OnHitEvent>.Where<Source>(warrior2).Listen(e => { Assert.Fail("Warrior2 only listener should not be called."); });

      // Create event: Source=w1 ONLY
      var slapEvent = new OnHitEvent();
      slapEvent.Set<Source>(warrior1);
      // Raise event
      EventBus<OnHitEvent>.Raise(slapEvent);

      // Only listeners matching the available parameters should be called
      Assert.False(warrior1_2EventCalled, "Listener for (Source=w1, Dest=w2) should NOT be called.");
      Assert.False(warrior2_1EventCalled, "Listener for (Dest=w2, Source=w1) should NOT be called.");
      Assert.False(warrior2EventCalled, "Listener for (Dest=w2) should NOT be called.");
      Assert.True(warrior1EventCalled, "Listener for (Source=w1) was not called.");
      Assert.True(noParameterEventCalled, "Generic listener was not called.");
    }

    [Test]
    public void TestEnum() // Placeholder - add specific enum tests if needed
    {
        bool enumCalled = false;
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
      var warrior1_2EventCalled = false;
      var warrior1EventCalled = false;
      var warrior2EventCalled = false;
      var warrior2_1EventCalled = false;
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

      var l6 = EventBus<OnHitEvent>.Where<Source>(warrior2).Listen(e => { Assert.Fail("Warrior 2 listener should never be called in this test"); });

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
      warrior1_2EventCalled = false; warrior2_1EventCalled = false; warrior2EventCalled = false; warrior1EventCalled = false; noParameterEventCalled = false; // Reset flags
      l1.Unsubscribe();
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.False(warrior1_2EventCalled, "After l1 unsub: l1 called");
      Assert.True(warrior2_1EventCalled, "After l1 unsub: l2 failed");
      Assert.True(warrior2EventCalled, "After l1 unsub: l3 failed");
      Assert.True(warrior1EventCalled, "After l1 unsub: l4 failed");
      Assert.True(noParameterEventCalled, "After l1 unsub: l5 failed");

      // --- Unsubscribe l2 and test ---
      warrior1_2EventCalled = false; warrior2_1EventCalled = false; warrior2EventCalled = false; warrior1EventCalled = false; noParameterEventCalled = false; // Reset flags
      l2.Unsubscribe();
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.False(warrior1_2EventCalled, "After l2 unsub: l1 called");
      Assert.False(warrior2_1EventCalled, "After l2 unsub: l2 called");
      Assert.True(warrior2EventCalled, "After l2 unsub: l3 failed");
      Assert.True(warrior1EventCalled, "After l2 unsub: l4 failed");
      Assert.True(noParameterEventCalled, "After l2 unsub: l5 failed");

      // --- Unsubscribe l3 and test ---
      warrior1_2EventCalled = false; warrior2_1EventCalled = false; warrior2EventCalled = false; warrior1EventCalled = false; noParameterEventCalled = false; // Reset flags
      l3.Unsubscribe();
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.False(warrior1_2EventCalled, "After l3 unsub: l1 called");
      Assert.False(warrior2_1EventCalled, "After l3 unsub: l2 called");
      Assert.False(warrior2EventCalled, "After l3 unsub: l3 called");
      Assert.True(warrior1EventCalled, "After l3 unsub: l4 failed");
      Assert.True(noParameterEventCalled, "After l3 unsub: l5 failed");


      // --- Unsubscribe l4 and test ---
      warrior1_2EventCalled = false; warrior2_1EventCalled = false; warrior2EventCalled = false; warrior1EventCalled = false; noParameterEventCalled = false; // Reset flags
      l4.Unsubscribe();
      EventBus<OnHitEvent>.Raise(slapEvent);
      Assert.False(warrior1_2EventCalled, "After l4 unsub: l1 called");
      Assert.False(warrior2_1EventCalled, "After l4 unsub: l2 called");
      Assert.False(warrior2EventCalled, "After l4 unsub: l3 called");
      Assert.False(warrior1EventCalled, "After l4 unsub: l4 called");
      Assert.True(noParameterEventCalled, "After l4 unsub: l5 failed");


      // --- Unsubscribe l5 and test ---
       warrior1_2EventCalled = false; warrior2_1EventCalled = false; warrior2EventCalled = false; warrior1EventCalled = false; noParameterEventCalled = false; // Reset flags
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