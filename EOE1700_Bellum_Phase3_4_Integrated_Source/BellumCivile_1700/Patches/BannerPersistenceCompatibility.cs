using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Patches
{
    internal static class BannerPersistenceCompatibility
    {
        private static bool _attempted;
        private static Func<object> _readConfig;
        private static Func<object, bool> _enabled;
        private static Func<object, bool> _protectNpcs;

        internal static void Initialize()
        {
            if (_attempted) return;
            Type module = AccessTools.TypeByName("BannerColorPersistence.PersistColorSubModule");
            if (module == null) return;
            _attempted = true;
            try
            {
                PropertyInfo config = AccessTools.Property(module, "Config");
                if (config?.GetGetMethod()?.IsStatic != true)
                    throw new InvalidOperationException("Unsupported Banner Color Persistence configuration API.");
                _enabled = BoolReader(config.PropertyType, "BannerColorPersistenceEnabled");
                _protectNpcs = BoolReader(config.PropertyType, "ShouldPreventNPCBannerColorChanges");
                _readConfig = Expression.Lambda<Func<object>>(Expression.Convert(
                    Expression.Property(null, config), typeof(object))).Compile();
                BellumCivileLogger.Log("Enabled Banner Color Persistence policy detection.");
            }
            catch (Exception ex) { Disable(ex); }
        }

        internal static bool ShouldPreserve(Clan clan)
        {
            if (clan == null || _readConfig == null) return false;
            try
            {
                object config = _readConfig();
                if (config == null || !_enabled(config)) return false;
                return _protectNpcs(config)
                    || (CharacterObject.PlayerCharacter != null && clan.Leader?.IsHumanPlayerCharacter == true);
            }
            catch (Exception ex)
            {
                Disable(ex);
                return false;
            }
        }

        private static Func<object, bool> BoolReader(Type type, string name)
        {
            var instance = Expression.Parameter(typeof(object), "config");
            return Expression.Lambda<Func<object, bool>>(
                Expression.Property(Expression.Convert(instance, type), name), instance).Compile();
        }

        private static void Disable(Exception exception)
        {
            _readConfig = null;
            // Leave unsupported builds to their own patches instead of guessing which clans they protect.
            BellumCivileLogger.Log($"Banner Color Persistence policy adapter unavailable: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
