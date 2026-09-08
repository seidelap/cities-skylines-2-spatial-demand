using System;
using Game.Buildings;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Unity.Entities;

namespace SpatialDemand.Mod
{
    internal readonly struct SellerPriceQuote
    {
        internal readonly double BasePrice, EmbeddedBuyCost, ServiceMultiplier, UnitPrice;
        internal SellerPriceQuote(double basePrice, double embeddedBuyCost, double serviceMultiplier)
        {
            BasePrice = basePrice; EmbeddedBuyCost = embeddedBuyCost; ServiceMultiplier = serviceMultiplier;
            UnitPrice = (basePrice + embeddedBuyCost) * serviceMultiplier;
        }
    }

    // Mirrors the public checkout price terms used by ResourceBuyerSystem. This is
    // a quote only: vanilla rounds the total and owns stock, cash and trade-cost updates.
    internal static class ShoppingPriceQuote
    {
        internal static bool TryGet(EntityManager manager, ResourcePrefabs prefabs,
            ref ComponentLookup<ResourceData> resourceData, Entity seller, Resource resource,
            bool retail, out SellerPriceQuote quote)
        {
            quote = default;
            if (resource == Resource.NoResource || !manager.Exists(seller)) return false;
            Entity resourcePrefab = prefabs[resource];
            if (!resourceData.HasComponent(resourcePrefab)) return false;
            float basePrice = retail ? EconomyUtils.GetMarketPrice(resource, prefabs, ref resourceData)
                : EconomyUtils.GetIndustrialPrice(resource, prefabs, ref resourceData);
            float embedded = manager.HasBuffer<TradeCost>(seller)
                ? EconomyUtils.GetTradeCost(resource, manager.GetBuffer<TradeCost>(seller, true)).m_BuyCost : 0;
            float multiplier = 1;
            if (retail && manager.HasComponent<ServiceAvailable>(seller) && manager.HasComponent<PropertyRenter>(seller))
            {
                if (!manager.HasComponent<PrefabRef>(seller)) return false;
                Entity prefab = manager.GetComponentData<PrefabRef>(seller).m_Prefab;
                if (!manager.HasComponent<ServiceCompanyData>(prefab)) return false;
                var data = manager.GetComponentData<ServiceCompanyData>(prefab);
                var service = manager.GetComponentData<ServiceAvailable>(seller);
                if (data.m_MaxService <= 0 || !NonNegative(service.m_ServiceAvailable)) return false;
                multiplier = EconomyUtils.GetServicePriceMultiplier(service.m_ServiceAvailable, data.m_MaxService);
            }
            if (!NonNegative(basePrice) || !NonNegative(embedded) || !NonNegative(multiplier)) return false;
            quote = new SellerPriceQuote(basePrice, embedded, multiplier);
            return NonNegative(quote.UnitPrice);
        }

        // EmbeddedBuyCost already includes the seller's historical upstream freight.
        // Add only the new supplier-to-buyer route cost when forecasting delivered inputs.
        internal static bool NonNegative(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
