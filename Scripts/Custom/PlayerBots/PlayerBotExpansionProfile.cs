using System;

namespace Server.CustomBots
{
    // Expansion labels are not enough to authorize behavior, but they are the
    // authoritative lower bound for whether a stock facet can be selected.
    // Callers still validate routes, regions, and content on the live map.
    internal static class PlayerBotExpansionProfile
    {
        private static readonly string[] T2AFacets = { "Felucca" };
        private static readonly string[] UORFacets = { "Felucca", "Trammel" };
        private static readonly string[] UOTDFacets = { "Felucca", "Trammel", "Ilshenar" };
        private static readonly string[] AOSFacets = { "Felucca", "Trammel", "Ilshenar", "Malas" };
        private static readonly string[] SEFacets = { "Felucca", "Trammel", "Ilshenar", "Malas", "Tokuno" };
        private static readonly string[] SAFacets = { "Felucca", "Trammel", "Ilshenar", "Malas", "Tokuno", "TerMur" };

        internal static string[] FacetNames
        {
            get
            {
                if (Core.SA) return SAFacets;
                if (Core.SE) return SEFacets;
                if (Core.AOS) return AOSFacets;
                if (Core.UOTD) return UOTDFacets;
                if (Core.UOR) return UORFacets;
                return Core.T2A ? T2AFacets : new string[0];
            }
        }

        internal static bool TryGetMap(string name, out Map map)
        {
            map = null;

            if (String.Equals(name, "Felucca", StringComparison.OrdinalIgnoreCase) && Core.T2A) map = Map.Felucca;
            else if (String.Equals(name, "Trammel", StringComparison.OrdinalIgnoreCase) && Core.UOR) map = Map.Trammel;
            else if (String.Equals(name, "Ilshenar", StringComparison.OrdinalIgnoreCase) && Core.UOTD) map = Map.Ilshenar;
            else if (String.Equals(name, "Malas", StringComparison.OrdinalIgnoreCase) && Core.AOS) map = Map.Malas;
            else if (String.Equals(name, "Tokuno", StringComparison.OrdinalIgnoreCase) && Core.SE) map = Map.Tokuno;
            else if (String.Equals(name, "TerMur", StringComparison.OrdinalIgnoreCase) && Core.SA) map = Map.TerMur;

            return map != null && map != Map.Internal;
        }

        internal static bool IsAvailable(Map map)
        {
            if (map == null || map == Map.Internal) return false;
            Map expected;
            return TryGetMap(map.Name, out expected) && expected == map;
        }

        internal static Map DefaultMap
        {
            get
            {
                Map map;
                return TryGetMap("Felucca", out map) ? map : null;
            }
        }
    }
}
