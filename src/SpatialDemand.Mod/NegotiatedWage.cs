using System;
using Colossal.Serialization.Entities;
using Game.Citizens;
using Game.Companies;
using Game.Common;
using Game.Agents;
using Game.Prefabs;
using Unity.Entities;

namespace SpatialDemand.Mod
{
    // The actual salary agreement, saved on the existing citizen. Money continues
    // to live only in vanilla Resources and TaxPayer; there is no second ledger.
    public struct NegotiatedWage : IComponentData, ISerializable
    {
        public Entity Employer;
        public int DailyGross;
        public byte JobLevel;
        public uint AcceptedFrame;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        { writer.Write(1); writer.Write(Employer); writer.Write(DailyGross); writer.Write(JobLevel); writer.Write(AcceptedFrame); }
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            if (version != 1) throw new InvalidOperationException("Unsupported negotiated wage version.");
            reader.Read(out Employer); reader.Read(out DailyGross); reader.Read(out JobLevel); reader.Read(out AcceptedFrame);
        }
    }

    internal static class ContractSalary
    {
        internal static bool Eligible(EntityManager manager, Entity citizen, Entity employer, int level)
        {
            if (level < 0 || level > 4 || !manager.HasComponent<Worker>(citizen) ||
                !manager.HasComponent<CompanyData>(employer) || !manager.HasComponent<PrefabRef>(employer) ||
                !manager.HasBuffer<Employee>(employer) || manager.HasComponent<Deleted>(employer) ||
                manager.HasComponent<Deleted>(citizen) || !manager.HasComponent<HouseholdMember>(citizen)) return false;
            var worker = manager.GetComponentData<Worker>(citizen);
            if (worker.m_Workplace != employer || worker.m_Level != level) return false;
            Entity prefab = manager.GetComponentData<PrefabRef>(employer).m_Prefab;
            if (!manager.HasComponent<IndustrialProcessData>(prefab) || manager.HasComponent<ExtractorCompanyData>(prefab) ||
                manager.HasComponent<StorageCompanyData>(prefab)) return false;
            Entity household = manager.GetComponentData<HouseholdMember>(citizen).m_Household;
            if (!manager.HasComponent<Game.Citizens.Household>(household) || manager.HasComponent<CommuterHousehold>(household) ||
                manager.HasComponent<TouristHousehold>(household) || manager.HasComponent<MovingAway>(household)) return false;
            foreach (var employee in manager.GetBuffer<Employee>(employer, true))
                if (employee.m_Worker == citizen && employee.m_Level == level) return true;
            return false;
        }

        internal static bool TryGet(EntityManager manager, Entity citizen, Worker worker, out int dailyGross)
        {
            dailyGross = 0;
            if (!manager.HasComponent<NegotiatedWage>(citizen) || !Eligible(manager, citizen, worker.m_Workplace, worker.m_Level)) return false;
            var contract = manager.GetComponentData<NegotiatedWage>(citizen);
            var household = manager.GetComponentData<HouseholdMember>(citizen).m_Household;
            if (contract.Employer != worker.m_Workplace || contract.JobLevel != worker.m_Level || contract.DailyGross <= 0 ||
                contract.DailyGross % Game.Simulation.PayWageSystem.kUpdatesPerDay != 0 ||
                !manager.HasComponent<Game.Citizens.Household>(household) || manager.HasComponent<CommuterHousehold>(household) ||
                manager.HasComponent<TouristHousehold>(household)) return false;
            dailyGross = contract.DailyGross;
            return true;
        }

        internal static int Get(EntityManager manager, Entity citizen, Worker worker, EconomyParameterData parameters)
            => NegotiatedPayrollHooks.Active && TryGet(manager, citizen, worker, out int wage) ? wage : parameters.GetWage(worker.m_Level);
    }
}
