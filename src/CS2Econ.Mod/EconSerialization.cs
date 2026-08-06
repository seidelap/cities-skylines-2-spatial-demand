// Mod-native save state (research notes §7): the inventory of state that has NO
// vanilla component to live in — parcel escrow + target configuration, per-exit
// trade EMAs, and the citywide migration/calibration scalars. Everything rides
// Colossal.Serialization.Entities ISerializable structs attached to entities
// (per-parcel, per-exit) or to one singleton buffer (globals), schema-versioned
// from day one after the game's own Game.Serialization.DataMigration precedent
// (notes §4 serialization family).
//
// The save round-trip spike is checklist item #1 (notes §9): attach one of each,
// save, reload, verify — and load the save WITHOUT the mod to confirm unknown
// component data is skipped, before ANY tier persists real state.
//
// Everything vanilla systems also read stays in VANILLA components
// (Game.Net.LandValue, BuildingCondition, PropertyRenter.m_Rent — notes §7
// architecture note); this file is only the mod-native remainder.

using System;
using System.Collections.Generic;
using CS2Econ.Core;
#if !OUT_OF_GAME_BUILD
using Unity.Entities;
// VERIFY-INGAME: namespace Colossal.Serialization.Entities is confirmed (notes
// §7), and the method shape Serialize<TWriter>/Deserialize<TReader> is quoted
// there; the constraint interface names IWriter/IReader are decompile knowledge.
// If the in-game compile fails on IWriter/IReader, the fix is these usings +
// the generic constraints below, nothing else. If the assembly is not resolved
// via Mod.props, add <Reference Include="Colossal.Serialization.Entities"
// Private="false" /> to the csproj's InGame item group.
using Colossal.Serialization.Entities;
#endif

namespace CS2Econ.Mod
{
    /// <summary>One schema version for all mod-native blobs. Evolution rule:
    /// fields are APPEND-ONLY — never reorder, never remove; each appended
    /// field is gated by "if (Version >= n)" in Deserialize and the constant
    /// bumps. An older save (smaller stored version) then reads cleanly under
    /// newer code. A newer save under older code cannot parse past its known
    /// fields — the §9-item-1 spike must confirm how the game treats surplus
    /// component bytes before any schema bump ships.</summary>
    public static class EconSchema
    {
        public const uint Version = 1;
    }

    // ---------------------------------------------------------------------
    // Per-parcel state (attached to the building/parcel entity).
    // Mirrors Parcel.{Escrow, TargetLevel, TargetUse, ScrapePressure} — the
    // §4.3/§4.4 redevelopment ledger that has no vanilla home.
    // ---------------------------------------------------------------------
    public struct ParcelEconState
#if !OUT_OF_GAME_BUILD
        : IComponentData, ISerializable
#endif
    {
        public uint Version;
        public float Escrow;          // earmarked wedge balance (TIF analog, §4.3)
        public byte TargetLevel;      // ℓ* of the winning configuration
        public byte TargetUse;        // (byte)ZoneKind of the winning configuration
        public short ScrapePressure;  // consecutive ticks the scrape gap held

#if !OUT_OF_GAME_BUILD
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(EconSchema.Version);
            writer.Write(Escrow);
            writer.Write(TargetLevel);
            writer.Write(TargetUse);
            writer.Write(ScrapePressure);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            if (Version >= 1)
            {
                reader.Read(out Escrow);
                reader.Read(out TargetLevel);
                reader.Read(out TargetUse);
                reader.Read(out ScrapePressure);
            }
            // if (Version >= 2) reader.Read(out NewField);   // append-only
        }
#endif
    }

    // ---------------------------------------------------------------------
    // Per-exit state: a BUFFER on the outside-connection entity, one element
    // per tradable resource — the engine keeps one TradeExit per (resource ×
    // connection), so a single component could only persist one of ~10.
    // Mirrors TradeExit.{SustainedQ, TransientB} — the finite-depth trade
    // position (design §4.5) that prices p(Q) = a ± t·(Q/ρ)^(1/d).
    // ---------------------------------------------------------------------
    public struct ExitEconState
#if !OUT_OF_GAME_BUILD
        : IBufferElementData, ISerializable
#endif
    {
        public uint Version;
        public byte ResourceIndex;    // (byte)TradeExit.Resource — the buffer key
        public float SustainedQ;      // EMA of drawn volume (the slow position)
        public float TransientB;      // burst layer, decays at resilience rate

#if !OUT_OF_GAME_BUILD
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(EconSchema.Version);
            writer.Write(ResourceIndex);
            writer.Write(SustainedQ);
            writer.Write(TransientB);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            if (Version >= 1)
            {
                reader.Read(out ResourceIndex);
                reader.Read(out SustainedQ);
                reader.Read(out TransientB);
            }
        }
#endif
    }

    // ---------------------------------------------------------------------
    // Global scalars: one singleton entity carrying a DynamicBuffer of tagged
    // (kind, index, value) entries — a key-value stream, so the set of scalars
    // can grow without a schema break (unknown kinds are ignored on load).
    // Covers: migration scalars per segment (reservation thresholds, out-signal
    // EMAs, attract EMAs), network memory, cumulative net inflow, and the
    // per-use calibration factors (§4.6).
    // ---------------------------------------------------------------------
    public enum EconGlobalKind : byte
    {
        ReservationThreshold = 1,   // index = segment
        OutSignalEma = 2,           // index = segment
        SegmentAttractEma = 3,      // index = segment
        NetworkMemory = 10,         // index = 0
        CumulativeNetInflow = 11,   // index = 0
        CalibFactor = 20,           // index = (byte)ZoneKind
        CalibSumRatio = 21,         // index = (byte)ZoneKind
        CalibN = 22,                // index = (byte)ZoneKind
    }

    /// <summary>Buffer element of the EconGlobalState singleton. VERIFY-INGAME:
    /// the §9-item-1 spike must confirm that a DynamicBuffer of ISerializable
    /// elements round-trips with its length handled by the framework (community
    /// pattern, notes §7 [unverified]); fallback is one packed singleton
    /// component serializing count + entries by hand.</summary>
    public struct EconGlobalState
#if !OUT_OF_GAME_BUILD
        : IBufferElementData, ISerializable
#endif
    {
        public uint Version;
        public byte Kind;             // EconGlobalKind
        public ushort Index;          // segment id / ZoneKind / 0
        public float Value;

#if !OUT_OF_GAME_BUILD
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(EconSchema.Version);
            writer.Write(Kind);
            writer.Write(Index);
            writer.Write(Value);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out Version);
            if (Version >= 1)
            {
                reader.Read(out Kind);
                reader.Read(out Index);
                reader.Read(out Value);
            }
        }
#endif
    }

    // ---------------------------------------------------------------------
    // Codec: WorldState ↔ serialization structs. Pure core logic — compiles
    // and is testable out-of-game; the reader/writer adapters call these at
    // load/save seams so the mapping lives in exactly one place.
    // ---------------------------------------------------------------------
    public static class EconStateCodec
    {
        public static ParcelEconState Capture(Parcel p) => new ParcelEconState
        {
            Version = EconSchema.Version,
            Escrow = (float)p.Escrow,
            TargetLevel = (byte)Math.Min(Math.Max(p.TargetLevel, 0), byte.MaxValue),
            TargetUse = (byte)p.TargetUse,
            ScrapePressure = (short)Math.Min(p.ScrapePressure, short.MaxValue),
        };

        public static void Restore(in ParcelEconState s, Parcel p)
        {
            p.Escrow = s.Escrow;
            p.TargetLevel = s.TargetLevel;
            p.TargetUse = (ZoneKind)s.TargetUse;
            p.ScrapePressure = s.ScrapePressure;
        }

        public static ExitEconState Capture(TradeExit e) => new ExitEconState
        {
            Version = EconSchema.Version,
            ResourceIndex = (byte)e.Resource,
            SustainedQ = (float)e.SustainedQ,
            TransientB = (float)e.TransientB,
        };

        public static void Restore(in ExitEconState s, TradeExit e)
        {
            e.SustainedQ = s.SustainedQ;
            e.TransientB = s.TransientB;
        }

        /// <summary>Flatten the global scalars into tagged entries (list is
        /// cleared first). Order is irrelevant — Unpack switches on Kind.</summary>
        public static void Pack(WorldState w, List<EconGlobalState> into)
        {
            into.Clear();
            void Add(EconGlobalKind kind, int index, double value) => into.Add(new EconGlobalState
            {
                Version = EconSchema.Version,
                Kind = (byte)kind,
                Index = (ushort)index,
                Value = (float)value,
            });

            var m = w.Migration;
            for (int s = 0; s < Segment.Count; s++)
            {
                Add(EconGlobalKind.ReservationThreshold, s, m.ReservationThreshold[s]);
                Add(EconGlobalKind.OutSignalEma, s, m.OutSignalEma[s]);
                Add(EconGlobalKind.SegmentAttractEma, s, m.SegmentAttractEma[s]);
            }
            Add(EconGlobalKind.NetworkMemory, 0, m.NetworkMemory);
            Add(EconGlobalKind.CumulativeNetInflow, 0, m.CumulativeNetInflow);

            foreach (var kv in w.Calibration.ByUse)
            {
                Add(EconGlobalKind.CalibFactor, (byte)kv.Key, kv.Value.Factor);
                Add(EconGlobalKind.CalibSumRatio, (byte)kv.Key, kv.Value.SumRatio);
                Add(EconGlobalKind.CalibN, (byte)kv.Key, kv.Value.N);
            }
        }

        /// <summary>Restore global scalars. Unknown kinds and out-of-range
        /// indices are ignored (forward compatibility — see EconSchema).</summary>
        public static void Unpack(IReadOnlyList<EconGlobalState> entries, WorldState w)
        {
            var m = w.Migration;
            CalibrationState.PerUse Cal(ushort idx)
            {
                var use = (ZoneKind)(byte)idx;
                if (!w.Calibration.ByUse.TryGetValue(use, out var s))
                    w.Calibration.ByUse[use] = s = new CalibrationState.PerUse();
                return s;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                switch ((EconGlobalKind)e.Kind)
                {
                    case EconGlobalKind.ReservationThreshold:
                        if (e.Index < Segment.Count) m.ReservationThreshold[e.Index] = e.Value; break;
                    case EconGlobalKind.OutSignalEma:
                        if (e.Index < Segment.Count) m.OutSignalEma[e.Index] = e.Value; break;
                    case EconGlobalKind.SegmentAttractEma:
                        if (e.Index < Segment.Count) m.SegmentAttractEma[e.Index] = e.Value; break;
                    case EconGlobalKind.NetworkMemory: m.NetworkMemory = e.Value; break;
                    case EconGlobalKind.CumulativeNetInflow: m.CumulativeNetInflow = e.Value; break;
                    case EconGlobalKind.CalibFactor: Cal(e.Index).Factor = e.Value; break;
                    case EconGlobalKind.CalibSumRatio: Cal(e.Index).SumRatio = e.Value; break;
                    case EconGlobalKind.CalibN: Cal(e.Index).N = e.Value; break;
                    default: break;   // unknown kind from a newer schema: skip
                }
            }
        }

#if !OUT_OF_GAME_BUILD
        /// <summary>List → singleton buffer (bridge/writer save seam).</summary>
        public static void CopyTo(List<EconGlobalState> src, DynamicBuffer<EconGlobalState> dst)
        {
            dst.Clear();
            for (int i = 0; i < src.Count; i++) dst.Add(src[i]);
        }

        /// <summary>Singleton buffer → list (bridge/reader load seam).</summary>
        public static void CopyFrom(DynamicBuffer<EconGlobalState> src, List<EconGlobalState> dst)
        {
            dst.Clear();
            for (int i = 0; i < src.Length; i++) dst.Add(src[i]);
        }
#endif
    }

#if !OUT_OF_GAME_BUILD
    /// <summary>The two save-state seams, wired: Capture stamps the codec's
    /// structs onto entities so they ride the NEXT game save (there is no
    /// notes-verified pre-save hook, so the bridge calls this periodically —
    /// at worst the save carries state CaptureEveryTicks engine ticks stale,
    /// well inside the EMAs' time constants); Restore pulls them back into a
    /// freshly built WorldState at the load seam (EconReader.BuildInitial).
    ///
    /// Shadow-mode discipline: the bridge gates Capture on ShadowAccountingOnly
    /// being OFF — observe-only sessions leave ZERO footprint in the save
    /// (stage-3 rule: shadow cannot corrupt anything). Until the first levying
    /// session, saves simply contain no mod components and Restore is a no-op.</summary>
    public static class EconStatePersistence
    {
        /// <summary>Engine ticks between captures. Cheap (a few thousand
        /// SetComponentData of tiny structs), but no reason to run every tick.</summary>
        public const int CaptureEveryTicks = 8;

        private static readonly List<EconGlobalState> _globalsScratch = new List<EconGlobalState>();
        private static readonly Dictionary<Entity, List<ExitEconState>> _exitScratch
            = new Dictionary<Entity, List<ExitEconState>>();

        public static void Capture(EntityManager em, EconReader reader, WorldState w)
        {
            // Parcels: one ParcelEconState per building/block entity.
            for (int k = 0; k < reader.ParcelEntities.Count; k++)
            {
                var e = reader.ParcelEntities[k];
                if (!em.Exists(e)) continue;
                var s = EconStateCodec.Capture(w.Parcels[reader.ParcelIds[k]]);
                if (em.HasComponent<ParcelEconState>(e)) em.SetComponentData(e, s);
                else em.AddComponentData(e, s);
            }

            // Exits: group the per-resource TradeExits sharing one connection
            // entity into that entity's ExitEconState buffer.
            _exitScratch.Clear();
            for (int i = 0; i < reader.ExitEntities.Count; i++)
            {
                var e = reader.ExitEntities[i];
                if (!em.Exists(e)) continue;
                if (!_exitScratch.TryGetValue(e, out var list))
                    _exitScratch[e] = list = new List<ExitEconState>();
                list.Add(EconStateCodec.Capture(w.Exits[i]));
            }
            foreach (var kv in _exitScratch)
            {
                var buf = em.HasBuffer<ExitEconState>(kv.Key)
                    ? em.GetBuffer<ExitEconState>(kv.Key)
                    : em.AddBuffer<ExitEconState>(kv.Key);
                buf.Clear();
                for (int i = 0; i < kv.Value.Count; i++) buf.Add(kv.Value[i]);
            }

            // Globals: the tagged-scalar stream on one singleton entity.
            EconStateCodec.Pack(w, _globalsScratch);
            var singleton = FindGlobalsSingleton(em);
            if (singleton == Entity.Null)
            {
                singleton = em.CreateEntity();
                em.AddBuffer<EconGlobalState>(singleton);
            }
            EconStateCodec.CopyTo(_globalsScratch, em.GetBuffer<EconGlobalState>(singleton));
        }

        public static void Restore(EntityManager em, EconReader reader, WorldState w)
        {
            for (int k = 0; k < reader.ParcelEntities.Count; k++)
            {
                var e = reader.ParcelEntities[k];
                if (!em.Exists(e) || !em.HasComponent<ParcelEconState>(e)) continue;
                EconStateCodec.Restore(em.GetComponentData<ParcelEconState>(e),
                                       w.Parcels[reader.ParcelIds[k]]);
            }
            for (int i = 0; i < reader.ExitEntities.Count; i++)
            {
                var e = reader.ExitEntities[i];
                if (!em.Exists(e) || !em.HasBuffer<ExitEconState>(e)) continue;
                var ex = w.Exits[i];
                var buf = em.GetBuffer<ExitEconState>(e, true);
                for (int j = 0; j < buf.Length; j++)
                    if (buf[j].ResourceIndex == (byte)ex.Resource)
                    { EconStateCodec.Restore(buf[j], ex); break; }
            }
            var singleton = FindGlobalsSingleton(em);
            if (singleton != Entity.Null)
            {
                EconStateCodec.CopyFrom(em.GetBuffer<EconGlobalState>(singleton, true), _globalsScratch);
                EconStateCodec.Unpack(_globalsScratch, w);
            }
        }

        private static Entity FindGlobalsSingleton(EntityManager em)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadWrite<EconGlobalState>());
            if (q.IsEmptyIgnoreFilter) return Entity.Null;
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            return ents.Length > 0 ? ents[0] : Entity.Null;
        }
    }
#endif
}
