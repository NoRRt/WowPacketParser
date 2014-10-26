using System;
using System.Collections.Generic;
using WowPacketParser.Enums;
using WowPacketParser.Misc;
using WowPacketParser.Parsing;
using WowPacketParser.Store;
using WowPacketParser.Store.Objects;
using CoreParsers = WowPacketParser.Parsing.Parsers;

namespace WowPacketParserModule.V6_0_2_19033.Parsers
{
    public static class UpdateHandler
    {
        [HasSniffData] // in ReadCreateObjectBlock
        [Parser(Opcode.SMSG_UPDATE_OBJECT)]
        public static void HandleUpdateObject(Packet packet)
        {
            var count = packet.ReadUInt32("NumObjUpdates");
            uint map = packet.ReadEntry<UInt16>(StoreNameType.Map, "MapID");
            packet.ResetBitReader();
            var bit552 = packet.ReadBit("bit552");
            if (bit552)
            {
                packet.ReadInt16("Int0");
                var int8 = packet.ReadUInt32("Int8");
                for (var i = 0; i < int8; i++)
                    packet.ReadPackedGuid128("Guid8");
            }
            packet.ReadUInt32("Unk");

            for (var i = 0; i < count; i++)
            {
                var type = packet.ReadByte();
                var typeString = ((UpdateTypeCataclysm)type).ToString();

                packet.AddValue("UpdateType", typeString, i);
                switch (typeString)
                {
                    case "Values":
                        {
                            var guid = packet.ReadPackedGuid128("GUID", i);

                            WoWObject obj;
                            var updates = CoreParsers.UpdateHandler.ReadValuesUpdateBlock(ref packet, guid.GetObjectType(), i, false);

                            if (Storage.Objects.TryGetValue(guid, out obj))
                            {
                                if (obj.ChangedUpdateFieldsList == null)
                                    obj.ChangedUpdateFieldsList = new List<Dictionary<int, UpdateField>>();
                                obj.ChangedUpdateFieldsList.Add(updates);
                            }

                            break;
                        }
                    case "CreateObject1":
                    case "CreateObject2": // Might != CreateObject1 on Cata
                        {
                            var guid = packet.ReadPackedGuid128("GUID", i);
                            ReadCreateObjectBlock(ref packet, guid, map, i);
                            break;
                        }
                    case "DestroyObjects":
                        {
                            CoreParsers.UpdateHandler.ReadObjectsBlock(ref packet, i);
                            break;
                        }
                }
            }
        }

        private static void ReadCreateObjectBlock(ref Packet packet, WowGuid guid, uint map, object index)
        {
            var objType = packet.ReadEnum<ObjectType>("Object Type", TypeCode.Byte, index);
            var moves = ReadMovementUpdateBlock(ref packet, guid, index);
            var updates = CoreParsers.UpdateHandler.ReadValuesUpdateBlock(ref packet, objType, index, true);

            WoWObject obj;
            switch (objType)
            {
                case ObjectType.Unit:
                    obj = new Unit();
                    break;
                case ObjectType.GameObject:
                    obj = new GameObject();
                    break;
                case ObjectType.Item:
                    obj = new Item();
                    break;
                case ObjectType.Player:
                    obj = new Player();
                    break;
                default:
                    obj = new WoWObject();
                    break;
            }

            obj.Type = objType;
            obj.Movement = moves;
            obj.UpdateFields = updates;
            obj.Map = map;
            obj.Area = CoreParsers.WorldStateHandler.CurrentAreaId;
            obj.PhaseMask = (uint)CoreParsers.MovementHandler.CurrentPhaseMask;
            obj.Phases = new HashSet<ushort>(CoreParsers.MovementHandler.ActivePhases);

            // If this is the second time we see the same object (same guid,
            // same position) update its phasemask
            if (Storage.Objects.ContainsKey(guid))
            {
                var existObj = Storage.Objects[guid].Item1;
                CoreParsers.UpdateHandler.ProcessExistingObject(ref existObj, obj, guid); // can't do "ref Storage.Objects[guid].Item1 directly
            }
            else
                Storage.Objects.Add(guid, obj, packet.TimeSpan);

            if (guid.HasEntry() && (objType == ObjectType.Unit || objType == ObjectType.GameObject))
                packet.AddSniffData(Utilities.ObjectTypeToStore(objType), (int)guid.GetEntry(), "SPAWN");
        }

        private static MovementInfo ReadMovementUpdateBlock(ref Packet packet, WowGuid guid, object index)
        {
            var moveInfo = new MovementInfo();

            packet.ResetBitReader();

            packet.ReadBit("NoBirthAnim", index);
            packet.ReadBit("EnablePortals", index);
            packet.ReadBit("PlayHoverAnim", index);
            packet.ReadBit("IsSuppressingGreetings", index);
            var HasMovementUpdate = packet.ReadBit("HasMovementUpdate", index);
            var HasMovementTransport = packet.ReadBit("HasMovementTransport", index);
            var Stationary = packet.ReadBit("Stationary", index);
            var CombatVictim = packet.ReadBit("HasCombatVictim", index);
            var ServerTime = packet.ReadBit("HasServerTime", index);
            var VehicleCreate = packet.ReadBit("HasVehicleCreate", index);
            var AnimKitCreate = packet.ReadBit("HasAnimKitCreate", index);
            var Rotation = packet.ReadBit("HasRotation", index);
            var AreaTrigger = packet.ReadBit("HasAreaTrigger", index);
            var GameObject = packet.ReadBit("GameObject", index);
            packet.ReadBit("ThisIsYou", index);
            packet.ReadBit("ReplaceActive", index);
            var SceneObjCreate = packet.ReadBit("SceneObjCreate", index);
            var ScenePendingInstances = packet.ReadBit("ScenePendingInstances", index);

            var PauseTimesCount = packet.ReadUInt32("PauseTimesCount", index);

            if (HasMovementUpdate) // 392
            {
                moveInfo = ReadMovementStatusData(ref packet, guid, index);

                packet.ReadSingle("WalkSpeed", index);
                packet.ReadSingle("RunSpeed", index);
                packet.ReadSingle("RunBackSpeed", index);
                packet.ReadSingle("SwimSpeed", index);
                packet.ReadSingle("SwimBackSpeed", index);
                packet.ReadSingle("FlightSpeed", index);
                packet.ReadSingle("FlightBackSpeed", index);
                packet.ReadSingle("TurnRate", index);
                packet.ReadSingle("PitchRate", index);

                var MovementForceCount = packet.ReadInt32("MovementForceCount", index);

                for (var i = 0; i < MovementForceCount; ++i)
                {
                    packet.ReadPackedGuid128("Id", index);
                    packet.ReadVector3("Direction", index);
                    packet.ReadInt32("TransportID", index);
                    packet.ReadSingle("Magnitude", index);
                    packet.ReadByte("Type", index);
                }

                packet.ResetBitReader();

                moveInfo.HasSplineData = packet.ReadBit("HasMovementSpline", index);

                if (moveInfo.HasSplineData)
                {
                    packet.ReadInt32("ID", index);
                    packet.ReadVector3("Destination", index);

                    packet.ResetBitReader();

                    var HasMovementSplineMove = packet.ReadBit("MovementSplineMove", index);
                    if (HasMovementSplineMove)
                    {
                        packet.ResetBitReader();

                        packet.ReadEnum<SplineFlag434>("SplineFlags", 25, index);
                        var type = (uint)packet.ReadEnum<SplineMode>("Mode", 2, index);

                        var HasJumpGravity = packet.ReadBit("HasJumpGravity", index);
                        var HasSpecialTime = packet.ReadBit("HasSpecialTime", index);

                        packet.ReadBits("Face", 2, index);

                        var HasSplineFilterKey = packet.ReadBit("HasSplineFilterKey", index);

                        packet.ReadUInt32("Elapsed", index);
                        packet.ReadUInt32("Duration", index);

                        packet.ReadSingle("DurationModifier", index);
                        packet.ReadSingle("NextDurationModifier", index);

                        var PointsCount = packet.ReadUInt32("PointsCount", index);

                        if (type == 3) // FaceDirection
                            packet.ReadSingle("FaceDirection", index);

                        if (type == 2) // FaceGUID
                            packet.ReadPackedGuid128("FaceGUID", index);

                        if (type == 1) // FaceSpot
                            packet.ReadVector3("FaceSpot", index);

                        if (HasJumpGravity)
                            packet.ReadSingle("JumpGravity", index);

                        if (HasSpecialTime)
                            packet.ReadInt32("SpecialTime", index);

                        if (HasSplineFilterKey)
                        {
                            var FilterKeysCount = packet.ReadUInt32("FilterKeysCount", index);
                            for (var i = 0; i < PointsCount; ++i)
                            {
                                packet.ReadSingle("In", index, i);
                                packet.ReadSingle("Out", index, i);
                            }

                            packet.ReadBits("FilterFlags", 2, index);
                        }

                        for (var i = 0; i < PointsCount; ++i)
                            packet.ReadVector3("Points", index, i);
                    }
                }
            }

            if (HasMovementTransport) // 456
            {
                moveInfo.TransportGuid = packet.ReadPackedGuid128("PassengerGUID", index);
                moveInfo.TransportOffset = packet.ReadVector4();
                var seat = packet.ReadByte("VehicleSeatIndex", index);
                packet.ReadUInt32("MoveTime", index);

                packet.ResetBitReader();

                var HasPrevMoveTime = packet.ReadBit("HasPrevMoveTime");
                var HasVehicleRecID = packet.ReadBit("HasVehicleRecID");

                if (HasPrevMoveTime)
                    packet.ReadUInt32("PrevMoveTime", index);

                if (HasVehicleRecID)
                    packet.ReadInt32("VehicleRecID", index);

                if (moveInfo.TransportGuid.HasEntry() && moveInfo.TransportGuid.GetHighType() == HighGuidType.Vehicle &&
                    guid.HasEntry() && guid.GetHighType() == HighGuidType.Creature)
                {
                    var vehicleAccessory = new VehicleTemplateAccessory();
                    vehicleAccessory.AccessoryEntry = guid.GetEntry();
                    vehicleAccessory.SeatId = seat;
                    Storage.VehicleTemplateAccessorys.Add(moveInfo.TransportGuid.GetEntry(), vehicleAccessory, packet.TimeSpan);
                }
            }

            if (Stationary) // 480
            {
                moveInfo.Position = packet.ReadVector3();
                moveInfo.Orientation = packet.ReadSingle();

                packet.AddValue("Stationary Position", moveInfo.Position, index);
            }

            if (CombatVictim) // 504
                packet.ReadPackedGuid128("CombatVictim Guid", index);

            if (ServerTime) // 516
                packet.ReadPackedTime("ServerTime", index);

            if (VehicleCreate) // 528
            {
                moveInfo.VehicleId = packet.ReadUInt32("RecID", index);
                packet.ReadSingle("InitialRawFacing", index);
            }

            if (AnimKitCreate) // 538
            {
                packet.ReadUInt16("AiID", index);
                packet.ReadUInt16("MovementID", index);
                packet.ReadUInt16("MeleeID", index);
            }

            if (Rotation) // 552
                packet.ReadPackedQuaternion("GameObject Rotation", index);

            if (AreaTrigger) // 772
            {
                // CliAreaTrigger
                packet.ReadInt32("ElapsedMs", index);

                packet.ReadVector3("RollPitchYaw1", index);

                packet.ResetBitReader();

                var HasAbsoluteOrientation = packet.ReadBit("HasAbsoluteOrientation", index);
                var HasDynamicShape = packet.ReadBit("HasDynamicShape", index);
                var HasAttached = packet.ReadBit("HasAttached", index);
                var HasFaceMovementDir = packet.ReadBit("HasFaceMovementDir", index);
                var HasFollowsTerrain = packet.ReadBit("HasFollowsTerrain", index);
                var HasTargetRollPitchYaw = packet.ReadBit("HasTargetRollPitchYaw", index);
                var HasScaleCurveID = packet.ReadBit("HasScaleCurveID", index);
                var HasMorphCurveID = packet.ReadBit("HasMorphCurveID", index);
                var HasFacingCurveID = packet.ReadBit("HasFacingCurveID", index);
                var HasMoveCurveID = packet.ReadBit("HasMoveCurveID", index);
                var HasAreaTriggerSphere = packet.ReadBit("HasAreaTriggerSphere", index);
                var HasAreaTriggerBox = packet.ReadBit("HasAreaTriggerBox", index);
                var HasAreaTriggerPolygon = packet.ReadBit("HasAreaTriggerPolygon", index);
                var HasAreaTriggerCylinder = packet.ReadBit("HasAreaTriggerCylinder", index);
                var HasAreaTriggerSpline = packet.ReadBit("HasAreaTriggerSpline", index);

                if (HasTargetRollPitchYaw)
                    packet.ReadVector3("TargetRollPitchYaw", index);

                if (HasScaleCurveID)
                    packet.ReadInt32("ScaleCurveID, index");

                if (HasMorphCurveID)
                    packet.ReadInt32("MorphCurveID", index);

                if (HasFacingCurveID)
                    packet.ReadInt32("FacingCurveID", index);

                if (HasMoveCurveID)
                    packet.ReadInt32("MoveCurveID", index);

                if (HasAreaTriggerSphere)
                {
                    packet.ReadSingle("Radius", index);
                    packet.ReadSingle("RadiusTarget", index);
                }

                if (HasAreaTriggerBox)
                {
                    packet.ReadVector3("Extents", index);
                    packet.ReadVector3("ExtentsTarget", index);
                }

                if (HasAreaTriggerPolygon)
                {
                    var VerticesCount = packet.ReadInt32("VerticesCount", index);
                    var VerticesTargetCount = packet.ReadInt32("VerticesTargetCount", index);
                    packet.ReadSingle("Height", index);
                    packet.ReadSingle("HeightTarget", index);

                    for (var i = 0; i < VerticesCount; ++i)
                        packet.ReadVector2("Vertices", index, i);

                    for (var i = 0; i < VerticesTargetCount; ++i)
                        packet.ReadVector2("VerticesTarget", index, i);
                }

                if (HasAreaTriggerCylinder)
                {
                    packet.ReadSingle("Radius", index);
                    packet.ReadSingle("RadiusTarget", index);
                    packet.ReadSingle("Height", index);
                    packet.ReadSingle("HeightTarget", index);
                    packet.ReadSingle("Float4", index);
                    packet.ReadSingle("Float5", index);
                }

                if (HasAreaTriggerSpline)
                {
                    packet.ReadInt32("TimeToTarget", index);
                    packet.ReadInt32("ElapsedTimeForMovement", index);
                    var int8 = packet.ReadInt32("VerticesCount", index);

                    for (var i = 0; i < int8; ++i)
                        packet.ReadVector3("Points", index, i);
                }
            }

            if (GameObject) // 788
            {
                packet.ReadInt32("WorldEffectID", index);

                packet.ResetBitReader();

                var bit8 = packet.ReadBit("bit8", index);
                if (bit8)
                    packet.ReadInt32("Int1", index);
            }

            if (SceneObjCreate) // 1184
            {
                packet.ResetBitReader();

                var CliSceneLocalScriptData = packet.ReadBit("CliSceneLocalScriptData", index);
                var PetBattleFullUpdate = packet.ReadBit("PetBattleFullUpdate", index);

                if (CliSceneLocalScriptData)
                {
                    packet.ResetBitReader();
                    var DataLength = packet.ReadBits(7);
                    packet.ReadWoWString("Data", DataLength, index);
                }

                if (PetBattleFullUpdate)
                {
                    for (var i = 0; i < 2; ++i)
                    {
                        packet.ReadPackedGuid128("CharacterID", index, i);

                        packet.ReadInt32("TrapAbilityID", index, i);
                        packet.ReadInt32("TrapStatus", index, i);

                        packet.ReadInt16("RoundTimeSecs", index, i);

                        packet.ReadByte("FrontPet", index, i);
                        packet.ReadByte("InputFlags", index, i);

                        packet.ResetBitReader();

                        var PetBattlePetUpdateCount = packet.ReadBits("PetBattlePetUpdateCount", 2, index, i);

                        for (var j = 0; j < PetBattlePetUpdateCount; ++j)
                        {
                            packet.ReadPackedGuid128("BattlePetGUID", index, i, j);

                            packet.ReadInt32("SpeciesID", index, i, j);
                            packet.ReadInt32("DisplayID", index, i, j);
                            packet.ReadInt32("CollarID", index, i, j);

                            packet.ReadInt16("Level", index, i, j);
                            packet.ReadInt16("Xp", index, i, j);


                            packet.ReadInt32("CurHealth", index, i, j);
                            packet.ReadInt32("MaxHealth", index, i, j);
                            packet.ReadInt32("Power", index, i, j);
                            packet.ReadInt32("Speed", index, i, j);
                            packet.ReadInt32("NpcTeamMemberID", index, i, j);

                            packet.ReadInt16("BreedQuality", index, i, j);
                            packet.ReadInt16("StatusFlags", index, i, j);

                            packet.ReadByte("Slot", index, i, j);

                            var PetBattleActiveAbility = packet.ReadInt32("PetBattleActiveAbility", index, i, j);
                            var PetBattleActiveAura = packet.ReadInt32("PetBattleActiveAura", index, i, j);
                            var PetBattleActiveState = packet.ReadInt32("PetBattleActiveState", index, i, j);

                            for (var k = 0; k < PetBattleActiveAbility; ++k)
                            {
                                packet.ReadInt32("AbilityID", index, i, j, k);
                                packet.ReadInt16("CooldownRemaining", index, i, j, k);
                                packet.ReadInt16("LockdownRemaining", index, i, j, k);
                                packet.ReadByte("AbilityIndex", index, i, j, k);
                                packet.ReadByte("Pboid", index, i, j, k);
                            }

                            for (var k = 0; k < PetBattleActiveAura; ++k)
                            {
                                packet.ReadInt32("AbilityID", index, i, j, k);
                                packet.ReadInt32("InstanceID", index, i, j, k);
                                packet.ReadInt32("RoundsRemaining", index, i, j, k);
                                packet.ReadInt32("CurrentRound", index, i, j, k);
                                packet.ReadByte("CasterPBOID", index, i, j, k);
                            }

                            for (var k = 0; k < PetBattleActiveState; ++k)
                            {
                                packet.ReadInt32("StateID", index, i, j, k);
                                packet.ReadInt32("StateValue", index, i, j, k);
                            }

                            packet.ResetBitReader();
                            var bits57 = packet.ReadBits(7);
                            packet.ReadWoWString("CustomName", bits57, index, i, j);
                        }
                    }

                    for (var i = 0; i < 3; ++i)
                    {
                        var PetBattleActiveAura = packet.ReadInt32("PetBattleActiveAura", index, i);
                        var PetBattleActiveState = packet.ReadInt32("PetBattleActiveState", index, i);

                        for (var j = 0; j < PetBattleActiveAura; ++j)
                        {
                            packet.ReadInt32("AbilityID", index, i, j);
                            packet.ReadInt32("InstanceID", index, i, j);
                            packet.ReadInt32("RoundsRemaining", index, i, j);
                            packet.ReadInt32("CurrentRound", index, i, j);
                            packet.ReadByte("CasterPBOID", index, i, j);
                        }

                        for (var j = 0; j < PetBattleActiveState; ++j)
                        {
                            packet.ReadInt32("StateID", index, i, j);
                            packet.ReadInt32("StateValue", index, i, j);
                        }
                    }

                    packet.ReadInt16("WaitingForFrontPetsMaxSecs", index);
                    packet.ReadInt16("PvpMaxRoundTime", index);

                    packet.ReadInt32("CurRound", index);
                    packet.ReadInt32("NpcCreatureID", index);
                    packet.ReadInt32("NpcDisplayID", index);

                    packet.ReadByte("CurPetBattleState");
                    packet.ReadByte("ForfeitPenalty");

                    packet.ReadPackedGuid128("InitialWildPetGUID");

                    packet.ReadBit("IsPVP");
                    packet.ReadBit("CanAwardXP");
                }
            }

            if (ScenePendingInstances) // 1208
            {
                var SceneInstanceIDs = packet.ReadInt32("SceneInstanceIDsCount");

                for (var i = 0; i < SceneInstanceIDs; ++i)
                    packet.ReadInt32("SceneInstanceIDs", index, i);
            }
            
            for (var i = 0; i < PauseTimesCount; ++i)
                packet.ReadInt32("PauseTimes", index, i);

            return moveInfo;
        }

        private static MovementInfo ReadMovementStatusData(ref Packet packet, WowGuid guid, object index)
        {
            var moveInfo = new MovementInfo();

            packet.ReadPackedGuid128("MoverGUID", index);

            packet.ReadUInt32("MoveIndex", index);
            packet.ReadVector4("Position", index);

            packet.ReadSingle("Pitch", index);
            packet.ReadSingle("StepUpStartElevation", index);

            var int152 = packet.ReadInt32("Int152", index);
            packet.ReadInt32("Int168", index);

            for (var i = 0; i < int152; i++)
                packet.ReadPackedGuid128("RemoveForcesIDs", index, i);

            packet.ResetBitReader();

            packet.ReadEnum<MovementFlag>("Movement Flags", 30, index);
            moveInfo.FlagsExtra = packet.ReadEnum<MovementFlagExtra>("Extra Movement Flags", 15, index);

            var HasTransport = packet.ReadBit("Has Transport Data", index);
            var HasFall = packet.ReadBit("Has Fall Data", index);
            packet.ReadBit("HasSpline", index);
            packet.ReadBit("HeightChangeFailed", index);

            if (HasTransport)
            {
                packet.ReadPackedGuid128("Transport Guid", index);
                packet.ReadVector4("Transport Position", index);
                packet.ReadSByte("Transport Seat", index);
                packet.ReadInt32("Transport Time", index);

                packet.ResetBitReader();

                var HasTransportTime2 = packet.ReadBit("HasTransportTime2", index);
                var HasTransportTime3 = packet.ReadBit("HasTransportTime3", index);

                if (HasTransportTime2)
                    packet.ReadUInt32("Transport Time 2", index);

                if (HasTransportTime3)
                    packet.ReadUInt32("Transport Time 3", index);
            }

            if (HasFall)
            {
                packet.ReadUInt32("Fall Time", index);
                packet.ReadSingle("JumpVelocity", index);

                packet.ResetBitReader();
                var bit20 = packet.ReadBit("Has Fall Direction", index);
                if (bit20)
                {
                    packet.ReadVector2("Fall", index);
                    packet.ReadSingle("Horizontal Speed", index);
                }
            }

            return moveInfo;
        }
    }
}