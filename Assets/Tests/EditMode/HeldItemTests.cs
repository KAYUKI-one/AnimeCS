using System.Linq;
using AnimeCS.Content;
using AnimeCS.Editor;
using AnimeCS.Match;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AnimeCS.Tests
{
    public sealed class HeldItemTests
    {
        [Test]
        public void EveryPlayableHeldItemProvidesAnEditablePoseAndAnAudibleCue()
        {
            var library = AssetDatabase.LoadAssetAtPath<HeldItemLibrary>(HeldItemDefaults.Path);
            Assert.That(library, Is.Not.Null);
            var ids = ContentLibrary.Catalog.Entries.Where(item => item && !item.Archived &&
                (item is WeaponDefinition || item is UtilityDefinition)).Select(item => item.Id).Append(HeldItemLibrary.ObjectiveId);
            foreach (var id in ids)
            {
                var item = library.Find(id);
                Assert.That(item, Is.Not.Null, id);
                Assert.That(item.inspectSound != null && item.activateSound != null, Is.True, id);
                Assert.That(item.inspect.Sample(item.inspect.seconds * .3f, out var sample), Is.True, id);
                Assert.That(sample.angles.sqrMagnitude, Is.GreaterThan(1), id);
                Assert.That(item.inspect.Sample(item.inspect.seconds, out _), Is.False, id);
            }
            var knife = library.Find(ContentLibrary.Catalog.Knife.Id);
            Assert.That(knife.light.Sample(.10f, out var light), Is.True);
            Assert.That(knife.heavy.Sample(.10f, out var heavy), Is.True);
            Assert.That(knife.rear.Sample(.10f, out var rear), Is.True);
            Assert.That(light.angles, Is.Not.EqualTo(heavy.angles));
            Assert.That(rear.angles, Is.Not.EqualTo(heavy.angles));
        }

        [Test]
        public void HandlingSnapshotRejectsTruncationAndPreservesActiveUtility()
        {
            var view = new SeatView { Held=HeldSlot.Utility, UtilityId="utility.echo", EquipTick=120, InspectTick=145 };
            var packet = new byte[SeatWire.MaxHandlingBytes];
            int size = SeatWire.WriteHandling(packet, view);
            var decoded = new SeatView { Held=HeldSlot.Utility };
            for (int cut = SeatWire.ExtendedStateBytes; cut < size; cut++)
                Assert.That(SeatWire.ReadHandling(packet,cut,ref decoded,default), Is.False);
            Assert.That(SeatWire.ReadHandling(packet,size,ref decoded,default), Is.True);
            Assert.That(decoded.UtilityId, Is.EqualTo(view.UtilityId));
            Assert.That(decoded.InspectTick, Is.EqualTo(view.InspectTick));
            Assert.That(decoded.EquipTick, Is.EqualTo(view.EquipTick));
        }
    }
}
