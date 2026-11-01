using NUnit.Framework;

namespace junklite.Tests
{
    public sealed class CameraLayerStackTests
    {
        private sealed class Cam { }

        [Test]
        public void TopPrefersCinematicThenZoneThenLane()
        {
            var stack = new CameraLayerStack<Cam>();
            Cam lane = new Cam(), zone = new Cam(), shot = new Cam();

            stack.Lane = lane;
            Assert.AreSame(lane, stack.Top);

            stack.SetZone(zone);
            Assert.AreSame(zone, stack.Top);

            int id = stack.PushCinematic(shot);
            Assert.AreSame(shot, stack.Top);

            // A lane change under an overlay does not take over.
            stack.Lane = new Cam();
            Assert.AreSame(shot, stack.Top);

            stack.ReleaseCinematic(id);
            Assert.AreSame(zone, stack.Top);
        }

        [Test]
        public void ClearZoneOnlyReleasesTheOwner()
        {
            var stack = new CameraLayerStack<Cam>();
            Cam a = new Cam(), b = new Cam();
            stack.SetZone(a);
            stack.SetZone(b);

            Assert.IsFalse(stack.ClearZone(a));
            Assert.AreSame(b, stack.Zone);
            Assert.IsTrue(stack.ClearZone(b));
            Assert.IsNull(stack.Zone);
        }

        [Test]
        public void LeaseReleasesExactlyOnce()
        {
            var stack = new CameraLayerStack<Cam>();
            int releases = 0;
            int id = stack.PushCinematic(new Cam());
            var lease = new CameraLayerStack<Cam>.Lease(id, i => { if (stack.ReleaseCinematic(i)) releases++; });

            lease.Dispose();
            lease.Dispose();

            Assert.AreEqual(1, releases);
            Assert.AreEqual(0, stack.CinematicCount);
        }

        [Test]
        public void OlderLeaseReleasedFirstKeepsNewestLive()
        {
            var stack = new CameraLayerStack<Cam>();
            Cam first = new Cam(), second = new Cam();
            int a = stack.PushCinematic(first);
            stack.PushCinematic(second);

            stack.ReleaseCinematic(a);
            Assert.AreSame(second, stack.Top);
        }

        [Test]
        public void ClearOverlaysDropsZoneAndCinematicsAndMakesLeasesNoOps()
        {
            var stack = new CameraLayerStack<Cam>();
            Cam lane = new Cam();
            stack.Lane = lane;
            stack.SetZone(new Cam());
            int id = stack.PushCinematic(new Cam());

            stack.ClearOverlays();

            Assert.AreSame(lane, stack.Top);
            Assert.IsFalse(stack.ReleaseCinematic(id));
        }
    }
}
