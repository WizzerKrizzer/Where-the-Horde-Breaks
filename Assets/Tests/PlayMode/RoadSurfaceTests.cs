using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class RoadSurfaceTests
    {
        [Test]
        public void FirstFourLevels_ShareThePrehistoricTilePackage()
        {
            var content = SampleContent.Create();
            var shared = EnvironmentTilePackages.Resolve(content.Levels[0].environmentTilePackage);
            Assert.That(shared.Id, Is.EqualTo(EnvironmentTilePackageId.Prehistoric));
            Assert.That(shared.UsesRoadMarkings, Is.False);
            for (var i = 0; i < 4; i++)
            {
                Assert.That(content.Levels[i].environmentTilePackage,
                    Is.EqualTo(EnvironmentTilePackageId.Prehistoric));
                Assert.That(EnvironmentTilePackages.Resolve(content.Levels[i].environmentTilePackage),
                    Is.SameAs(shared), $"Level {i + 1} should reuse the same package definition.");
            }
        }

        [UnityTest]
        public IEnumerator LevelTwoMap_BuildsOnlyGroundAndRoad()
        {
            yield return SceneManager.LoadSceneAsync("Main");
            var bootstrap = Object.FindAnyObjectByType<TowerDefenseBootstrap>();
            var level = SampleContent.Create().Levels[1];
            typeof(TowerDefenseBootstrap).GetMethod("LoadLevelMap", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(bootstrap, new object[] { level });
            yield return null;

            var road = Object.FindAnyObjectByType<RoadSurface>();
            Assert.That(road.PackageId, Is.EqualTo(EnvironmentTilePackageId.Prehistoric));
            Assert.That(road.UsesRoadMarkings, Is.False);
            var map = GameObject.Find("LevelMap");
            Assert.That(map, Is.Not.Null);
            Assert.That(map.GetComponentsInChildren<Renderer>().Length, Is.EqualTo(2),
                "Placeholder map is only the ground support and the road surface; no scenery.");
            Assert.That(road.GetComponentsInChildren<Renderer>().Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Roads_RenderContinuousJunctionsAndMatchNavigation()
        {
            yield return SceneManager.LoadSceneAsync("Main");
            var bootstrap = Object.FindAnyObjectByType<TowerDefenseBootstrap>();
            var content = SampleContent.Create();
            var camera = Camera.main;
            Object.FindAnyObjectByType<TopDownCameraController>().enabled = false;
            var target = new RenderTexture(1600, 1000, 24);
            var previousTarget = camera.targetTexture;
            camera.targetTexture = target;
            Directory.CreateDirectory("Logs/RoadReview");
            try
            {
                foreach (var levelIndex in new[] { 1, 5 })
                {
                    var level = content.Levels[levelIndex];
                    var begin = Time.realtimeSinceStartupAsDouble;
                    var route = (PathRoute)typeof(TowerDefenseBootstrap).GetMethod("LoadLevelMap", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(bootstrap, new object[] { level });
                    var buildSeconds = Time.realtimeSinceStartupAsDouble - begin;
                    yield return null; // old map / owned materials are destroyed
                    var road = Object.FindAnyObjectByType<RoadSurface>();
                    Assert.That(road, Is.Not.Null);
                    Assert.That(road.UsesRoadMarkings, Is.False);
                    Assert.That(road.GetComponentsInChildren<Renderer>().Length, Is.EqualTo(1), "Only the flat route surface is rendered.");
                    foreach (var renderer in road.GetComponentsInChildren<Renderer>())
                    {
                        Assert.That(renderer.sharedMaterial.shader.isSupported, Is.True);
                        Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("TowerDefense/WeatheredRoad"));
                    }
                    Assert.That(GameObject.Find("PathBoundary_Left"), Is.Null);
                    Assert.That(GameObject.Find("HordeWalkableBoundaryDebug"), Is.Null);
                    var flow = new HordeFlowField(route.Waypoints, route.SecondaryWaypoints, level.roadWidth*.5f, .25f,
                        route.WaypointHalfWidths, route.SecondaryWaypointHalfWidths, route.TertiaryWaypoints,
                        route.TertiaryWaypointHalfWidths, route.UseSquareCorners);
                    var bounds = road.WorldBounds;
                    var checkedPoints = 0;
                    for (var z = bounds.yMin; z < bounds.yMax; z += .71f)
                    for (var x = bounds.xMin; x < bounds.xMax; x += .73f)
                    {
                        var d = road.SignedDistance(new Vector2(x,z));
                        if (Mathf.Abs(d) < .3f) continue; // raster interpolation band
                        Assert.That(flow.IsWalkable(new Vector3(x,0,z)), Is.EqualTo(d > 0), $"Footprint mismatch {x},{z}");
                        checkedPoints++;
                    }
                    var info = $"Level {levelIndex+1}: road build {buildSeconds:F3}s; 1 renderer; {checkedPoints} navigation samples agree.";
                    File.AppendAllText("Logs/RoadReview/validation.txt", info + "\n");
                    var focuses = levelIndex == 1
                        ? new[] { Vector3.zero, new Vector3(40,0,0), new Vector3(0,0,22) }
                        : new[] { Vector3.zero, route.Waypoints[2] };
                    for (var i = 0; i < focuses.Length; i++)
                    {
                        var height = i == 0 ? 78f : 12f;
                        camera.transform.rotation = Quaternion.Euler(65,0,0);
                        camera.transform.position = focuses[i] - camera.transform.forward * (height / Mathf.Sin(65*Mathf.Deg2Rad));
                        yield return null;
                        camera.Render();
                        var previous = RenderTexture.active;
                        RenderTexture.active = target;
                        var image = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
                        image.Apply();
                        File.WriteAllBytes($"Logs/RoadReview/level-{levelIndex+1}-view-{i}.png",image.EncodeToPNG());
                        Object.Destroy(image);
                        RenderTexture.active = previous;
                    }
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                target.Release();
                Object.Destroy(target);
            }
        }
    }
}
