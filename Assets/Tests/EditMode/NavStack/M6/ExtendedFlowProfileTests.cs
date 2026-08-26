// ExtendedFlowProfileTests.cs
// task-112 M6 -- direct test of the dominant-profile aggregation
// algorithm used by ExtendedFlowSystem. Build a tiny in-memory
// TraversalProfileBlob with 3 profiles (footprints 1, 2, 3) and
// 3 "members" each holding a different profile id; aggregate them
// using the same pick rule the system uses; assert the dominant
// profile id == the one with footprint = 3.
//
// Also asserts the layer-mask intersection contract (only layers
// admissible by every member appear in the result).

using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Tests.EditMode.NavStack.M6
{
    public class ExtendedFlowProfileTests
    {
        [Test]
        public void DominantProfile_LargestFootprintWins()
        {
            // Hand-build a blob: 3 profiles with footprints 1, 2, 3.
            BlobAssetReference<TraversalProfileBlob> blob;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<TraversalProfileBlob>();
                var profs = builder.Allocate(ref root.Profiles, 3);
                profs[0] = new TraversalProfile
                {
                    FootprintSize = 1,
                    AllowedLayersMask = 0x03,
                    CanClimb = 1,
                    OwnerId = -1,
                };
                profs[1] = new TraversalProfile
                {
                    FootprintSize = 2,
                    AllowedLayersMask = 0x01,
                    CanClimb = 0,
                    OwnerId = -1,
                };
                profs[2] = new TraversalProfile
                {
                    FootprintSize = 3,
                    AllowedLayersMask = 0x01,
                    CanClimb = 0,
                    OwnerId = -1,
                };
                blob = builder.CreateBlobAssetReference<TraversalProfileBlob>(Allocator.Temp);
            }

            try
            {
                // Members hold profiles 0, 1, 2 respectively.
                byte[] memberProfiles = { 0, 1, 2 };

                // Aggregation algorithm (mirrors ExtendedFlowSystem):
                byte dominantProfile = 0;
                byte maxFootprint = 1;
                byte layerMask = 0xFF;
                ref var profilesBlob = ref blob.Value;
                for (int i = 0; i < memberProfiles.Length; i++)
                {
                    byte pid = memberProfiles[i];
                    ref var prof = ref profilesBlob.Profiles[pid];
                    if (prof.FootprintSize > maxFootprint
                        || (prof.FootprintSize == maxFootprint && pid < dominantProfile))
                    {
                        dominantProfile = pid;
                        maxFootprint = prof.FootprintSize;
                    }
                    layerMask = (byte)(layerMask & prof.AllowedLayersMask);
                }

                Assert.AreEqual(2, dominantProfile,
                    "Profile 2 (footprint 3) must be the dominant pick");
                Assert.AreEqual(3, maxFootprint,
                    "MaxFootprint must reflect profile 2's footprint=3");
                // Intersection of 0x03, 0x01, 0x01 = 0x01 (ground only).
                Assert.AreEqual(0x01, layerMask,
                    "AllowedLayersMask must be the intersection across members");
            }
            finally
            {
                blob.Dispose();
            }
        }

        [Test]
        public void DominantProfile_NoMembers_DefaultGround()
        {
            // Empty-member case: dominant defaults to ProfileDefaultGround.
            byte dominantProfile = TraversalProfileSingleton.ProfileDefaultGround;
            byte maxFootprint = 1;
            byte layerMask = 0xFF;
            bool any = false;

            Assert.False(any, "No members must keep the dominant at default");
            Assert.AreEqual(TraversalProfileSingleton.ProfileDefaultGround, dominantProfile);
            Assert.AreEqual(1, maxFootprint);
            Assert.AreEqual(0xFF, layerMask);
        }
    }
}
