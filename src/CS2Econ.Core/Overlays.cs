using System;

namespace CS2Econ.Core
{
    /// <summary>§4.7: the overlay is the build model rendered before building.
    /// Pure projections over engine state — nothing here mutates anything, and
    /// the calibration loop measures these numbers against what then happens.</summary>
    public static class Overlays
    {
        /// <summary>Expected rent/sqft-analog for a zone type at a cluster
        /// (calibration-corrected — the same number construction trusts).</summary>
        public static double ExpectedRent(WorldState w, AccessState acc, TradeSystem trade,
                                          int cluster, ZoneKind use, double[] segmentPresence, EconParams p)
        {
            int lvl = 2;
            return LandAccounting.BidPerUnit(acc, trade, cluster, use, lvl, segmentPresence, p,
                                             LandAccounting.UnitsFor(use))
                   * w.Calibration.Factor(use);
        }

        /// <summary>Predicted time-to-fill (ticks) for one more unit of a use at a
        /// cluster, from residual demand vs typical absorption pace.</summary>
        public static double TimeToFill(WorldState w, ResidualDemand residuals, int cluster, ZoneKind use, int units)
        {
            double r = residuals.Get(cluster, use, w.Claims);
            if (r <= 0) return double.PositiveInfinity;
            return units / r * 30.0;   // residual replenishes on roughly a 30-tick horizon
        }

        /// <summary>Redevelopment pressure: the ℓ − ℓ* gap plus escrow fill — the
        /// gentrification frontier as charging bars.</summary>
        public static (int levelGap, double escrowFill) Redevelopment(Parcel pl, EconParams p)
        {
            int gap = pl.TargetLevel - pl.Level;
            double cost;
            if (pl.TargetIsScrape)
            {
                int newUnits = LandAccounting.UnitsFor(pl.TargetUse);
                cost = Math.Max(0, p.DemolitionPerUnit * pl.Units + p.RC(pl.TargetLevel, newUnits)
                       - p.SalvageFraction * pl.Condition * p.RC(pl.Level, pl.Units));
            }
            else cost = Math.Max(0, p.RC(pl.TargetLevel, pl.Units) - pl.Condition * p.RC(pl.Level, pl.Units));
            return (gap, cost > 0 ? MathUtil.Clamp(pl.Escrow / cost, 0, 1) : 0);
        }

        /// <summary>Payment decomposition for the tooltip: S / tax / wedge —
        /// "why is my rent high": location vs structure vs neglect (§4.7).</summary>
        public static (double s, double tax, double wedge) PaymentDecomposition(Parcel pl, EconParams p)
        {
            if (pl.Units == 0) return (0, 0, 0);
            double s = LandAccounting.SPerUnit(pl.Level, pl.Condition, p);
            double taxPart = p.CaptureFraction * Math.Max(0, pl.CurrentResidual) / pl.Units;
            double wedgePart = p.CaptureFraction * pl.Wedge / pl.Units;
            return (s, taxPart, wedgePart);
        }

        /// <summary>Net fiscal yield per cluster: land+income tax revenue minus
        /// service cost — never gross, which teaches fiscal-zoning pathology.</summary>
        public static double NetFiscalYield(WorldState w, int cluster, double landRevenue,
                                            double incomeRevenue, EconParams p)
        {
            double serviceCost = 0;
            foreach (int pi in w.ParcelsByCluster[cluster])
            {
                var pl = w.Parcels[pi];
                if (pl.State != ParcelState.Built) continue;
                if (pl.IsResidential) serviceCost += pl.OccupantHouseholds.Count * p.ServiceCostPerHousehold;
                else if (pl.OccupantFirm >= 0) serviceCost += pl.Units * p.ServiceCostPerFirmSlot;
            }
            return landRevenue + incomeRevenue - serviceCost;
        }
    }
}
