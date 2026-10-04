using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class CrowdOrderAuditTests
    {
        [UnityTest]
        public IEnumerator SameSeed_ForwardRepeatReverse_AfterOpeningSpace()
        {
            var preference = PlayerPrefs.GetInt(GoblinPreviewAssets.PreferenceKey, GoblinPreviewAssets.DefaultEnabled);
            PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 1);
            Directory.CreateDirectory("Logs/CrowdEnvelope");
            var summary = new System.Text.StringBuilder();
            try
            {
                var field = new HordeFlowField(
                    new[] { new Vector3(22,0,0), new Vector3(44,0,0), new Vector3(64,0,0) },
                    new[] { new Vector3(22,0,19), new Vector3(34,0,12), new Vector3(44,0,0), new Vector3(64,0,0) },
                    2.85f, .62f, new[] {2.85f,2.85f,2.85f}, new[] {2.85f,2.85f,2.85f,2.85f},
                    new[] { new Vector3(22,0,-19), new Vector3(34,0,-12), new Vector3(44,0,0), new Vector3(64,0,0) },
                    new[] {2.85f,2.85f,2.85f,2.85f});
                var seed = new List<GpuHordeSimulation.AgentState>();
                for (var row=0; row<65; row++) for (var col=0; col<28; col++)
                {
                    var p=new Vector2(32+col*.51f+(row%2)*.255f,-17+row*.443f);
                    if (!field.IsWalkable(new Vector3(p.x,0,p.y))) continue;
                    seed.Add(new GpuHordeSimulation.AgentState { Position=p, StepStartPosition=p,
                        Scale=.22f, Status=1, Health=100, Mass=1.3f, SlowMultiplier=1, Facing=Vector2.right });
                }
                var controls=new Vector4[seed.Count];
                for(var i=0;i<controls.Length;i++) controls[i]=new Vector4(4.8f,1,0,0);
                for(var trial=0;trial<1;trial++)
                {
                    var states=seed.ToArray();
                    Assert.That(CrowdMergeValidationTests.Pairs(states).pairs, Is.Zero);
                    Assert.That(GpuHordeSimulation.TryCreate(states.Length,field,EnemyManager.GetLowEnemyMesh(),out var simulation),Is.True);
                    using(simulation)
                    {
                        simulation.SetCollisionDiameter(.203f*GoblinPreviewAssets.ContactRadiusPerScale/.5f);
                        simulation.SpawnBatch(states,controls,0,states.Length);
                        var trace=new System.Text.StringBuilder("tick,id,x,z,dx,dz,gait,alive\n");
                        var maximumPairs=0; var meanSpeed=0f; var firstTickMoved=0; var releasedCount=0;
                        var releasePositions=new Vector2[states.Length];
                        for(var tick=0;tick<300;tick++)
                        {
                            if(tick==180)
                            {
                                // Remove the outlet's leading bodies; survivor positions are unchanged.
                                for(var i=0;i<states.Length;i++)
                                {
                                    if(states[i].Position.x>43) states[i].Status=0;
                                    else if(states[i].Status==1) releasedCount++;
                                    releasePositions[i]=states[i].Position;
                                }
                                simulation.SpawnBatch(states,controls,0,states.Length);
                                summary.AppendLine($"trial={trial} release-grid={GridSignal(states):F4} survivors={releasedCount}");
                            }
                            simulation.Dispatch(1f/60,null,null,false,.5f,(tick&3)==3);
                            simulation.ReadStatesSynchronous(states,states.Length);
                            maximumPairs=Math.Max(maximumPairs,CrowdMergeValidationTests.Pairs(states).pairs);
                            for(var i=0;i<states.Length;i++)
                            {
                                var d=states[i].Position-states[i].StepStartPosition;
                                if(tick==180 && states[i].Status==1 && d.magnitude>.003f) firstTickMoved++;
                                if(i % Math.Max(1,states.Length/24)==0)
                                    trace.AppendLine(FormattableString.Invariant($"{tick},{i},{states[i].Position.x},{states[i].Position.y},{d.x},{d.y},{states[i].AnimationDistance},{states[i].Status}"));
                                if(tick>=240 && states[i].Status==1) meanSpeed+=d.magnitude/releasedCount;
                            }
                            if(tick%30==0) yield return null;
                        }
                        summary.AppendLine($"trial={trial} solver=xpbd maxPairs={maximumPairs} firstTickMoved={firstTickMoved}/{releasedCount} lastSecondSpeed={meanSpeed:F3} final-grid={GridSignal(states):F4}");
                        File.WriteAllText($"Logs/CrowdEnvelope/order-{trial}-trace.csv",trace.ToString());
                        File.WriteAllLines($"Logs/CrowdEnvelope/order-{trial}-positions.csv",Array.ConvertAll(states,state=>FormattableString.Invariant($"{state.Position.x},{state.Position.y},{state.Status}")));
                        Assert.That(maximumPairs,Is.Zero);
                        Assert.That(meanSpeed,Is.GreaterThan(.8f));
                    }
                }
            }
            finally
            {
                File.WriteAllText("Logs/CrowdEnvelope/order-summary.txt",summary.ToString());
                Debug.Log(summary.ToString());
                PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey,preference);
            }
        }

        // Coherence at the actual 0.62 m GPU cell period, along both world axes.
        // Report a signal, not an assertion that any regular dense packing is a bug.
        private static float GridSignal(GpuHordeSimulation.AgentState[] states)
        {
            var x=Vector2.zero;var y=Vector2.zero;var count=0;
            foreach(var state in states)
            {
                if(state.Status!=1) continue;
                var px=state.Position.x*Mathf.PI*2/.62f;var py=state.Position.y*Mathf.PI*2/.62f;
                x+=new Vector2(Mathf.Cos(px),Mathf.Sin(px)); y+=new Vector2(Mathf.Cos(py),Mathf.Sin(py)); count++;
            }
            return Mathf.Max(x.magnitude,y.magnitude)/Mathf.Max(1,count);
        }
    }
}
