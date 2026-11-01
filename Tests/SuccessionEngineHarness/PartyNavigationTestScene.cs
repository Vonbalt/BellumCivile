using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Serialization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;

// Supply only the map boundary to real native navigation helpers, without starting the engine.
internal sealed class PartyNavigationTestScene : RealProxy
{
    internal readonly HashSet<Vec2> InvalidPositions = new HashSet<Vec2>();
    internal readonly HashSet<Vec2> DisconnectedPositions = new HashSet<Vec2>();
    internal bool PathsAvailable = true, ThrowOnPath;
    internal int PathCalls;
    internal float LastMaximumDistance;
        internal float PathDetour;
    internal int[] LastExcludedTerrain;

    internal PartyNavigationTestScene() : base(AccessTools.Field(typeof(Campaign), "_mapSceneWrapper").FieldType) { }

    internal Campaign CreateCampaign()
    {
        var campaign = (Campaign)FormatterServices.GetUninitializedObject(typeof(Campaign));
        var models = (GameModels)FormatterServices.GetUninitializedObject(typeof(GameModels));
        AccessTools.PropertySetter(typeof(GameModels), "PartyNavigationModel").Invoke(models, new object[] { new DefaultPartyNavigationModel() });
        AccessTools.PropertySetter(typeof(GameModels), "MapDistanceModel").Invoke(models, new object[] { new DefaultMapDistanceModel() });
        AccessTools.Field(typeof(Campaign), "_gameModels").SetValue(campaign, models);
        AccessTools.Field(typeof(Campaign), "_mapSceneWrapper").SetValue(campaign, GetTransparentProxy());
        AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
        return campaign;
    }

    public override IMessage Invoke(IMessage message)
    {
        var call = (IMethodCallMessage)message;
        object[] args = (object[])call.Args.Clone();
        object result;
        switch (call.MethodName)
        {
            case "GetFaceIndex":
                var point = (CampaignVec2)args[0];
                result = InvalidPositions.Contains(point.ToVec2()) || !point.ToVec2().IsValid
                    ? PathFaceRecord.NullFaceRecord : new PathFaceRecord(1, (int)TerrainType.Plain, 1);
                break;
            case "GetFaceTerrainType": result = TerrainType.Plain; break;
            case "GetMapBorders":
                args[0] = Vec2.Zero; args[1] = new Vec2(1000, 1000); args[2] = 1000f; result = null;
                break;
            case "GetPathDistanceBetweenAIFaces":
                if (ThrowOnPath) return new ReturnMessage(new InvalidOperationException("Navigation unavailable"), call);
                PathCalls++; LastMaximumDistance = (float)args[5]; LastExcludedTerrain = (int[])args[7];
                args[6] = ((Vec2)args[2]).Distance((Vec2)args[3]) + PathDetour;
                result = PathsAvailable && !DisconnectedPositions.Contains((Vec2)args[2])
                    && !DisconnectedPositions.Contains((Vec2)args[3]) && (float)args[6] <= (float)args[5];
                break;
            default: return new ReturnMessage(new NotSupportedException(call.MethodName), call);
        }
        // Do not copy readonly coordinate arguments back into their caller's cached structs.
        bool hasOutput = call.MethodName == "GetMapBorders" || call.MethodName == "GetPathDistanceBetweenAIFaces";
        return new ReturnMessage(result, hasOutput ? args : null, hasOutput ? args.Length : 0, call.LogicalCallContext, call);
    }
}
