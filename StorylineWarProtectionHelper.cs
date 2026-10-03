using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile
{
    /// <summary>
    /// Keeps Bellum's peace machinery out of Story Mode's third-phase coalition wars.
    /// StoryMode is optional, so the quest API is resolved once through reflection.
    /// </summary>
    internal static class StorylineWarProtectionHelper
    {
        private const string StoryModeManagerTypeName = "StoryMode.StoryModeManager";

        private static bool _reflectionInitialized;
        private static bool _reflectionFailureLogged;
        private static PropertyInfo _currentProperty;
        private static PropertyInfo _mainStoryLineProperty;
        private static PropertyInfo _thirdPhaseProperty;
        private static PropertyInfo _oppositionKingdomsProperty;
        private static PropertyInfo _thirdPhaseCompletedProperty;

        internal static bool TryGetPeaceBlock(Kingdom first, Kingdom second, out TextObject reason)
        {
            reason = TextObject.GetEmpty();
            if (!IsPeaceBlocked(first, second))
                return false;

            reason = new TextObject("{=BC_StorylineWar_PeaceBlocked}The active campaign quest requires this war to continue until one side is decisively defeated.");
            return true;
        }

        internal static bool IsPeaceBlocked(Kingdom first, Kingdom second)
        {
            if (first == null || second == null || first == second)
                return false;

            // Match Story Mode's own political scope: exactly one belligerent must be the
            // main hero's current map faction, and the other must be a registered opposition realm.
            Kingdom playerKingdom = Hero.MainHero?.MapFaction as Kingdom;
            if (playerKingdom == null || (first == playerKingdom) == (second == playerKingdom))
                return false;

            Kingdom opponent = first == playerKingdom ? second : first;
            if (!TryGetActiveThirdPhase(out object thirdPhase))
                return false;

            try
            {
                object oppositionKingdoms = _oppositionKingdomsProperty.GetValue(thirdPhase, null);
                if (!(oppositionKingdoms is IEnumerable enumerable))
                    return false;

                foreach (object entry in enumerable)
                {
                    if (entry == opponent)
                        return true;

                    if (entry is Kingdom kingdom
                        && !string.IsNullOrWhiteSpace(kingdom.StringId)
                        && kingdom.StringId == opponent.StringId)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogReflectionFailureOnce("could not read Story Mode opposition kingdoms", ex);
            }

            return false;
        }

        private static bool TryGetActiveThirdPhase(out object thirdPhase)
        {
            thirdPhase = null;
            EnsureReflectionInitialized();
            if (_currentProperty == null
                || _mainStoryLineProperty == null
                || _thirdPhaseProperty == null
                || _oppositionKingdomsProperty == null)
            {
                return false;
            }

            try
            {
                object manager = _currentProperty.GetValue(null, null);
                object mainStoryLine = manager == null ? null : _mainStoryLineProperty.GetValue(manager, null);
                thirdPhase = mainStoryLine == null ? null : _thirdPhaseProperty.GetValue(mainStoryLine, null);
                if (thirdPhase == null)
                    return false;

                if (_thirdPhaseCompletedProperty?.PropertyType == typeof(bool)
                    && (bool)_thirdPhaseCompletedProperty.GetValue(thirdPhase, null))
                {
                    thirdPhase = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogReflectionFailureOnce("could not inspect Story Mode's active third phase", ex);
                thirdPhase = null;
                return false;
            }
        }

        private static void EnsureReflectionInitialized()
        {
            if (_reflectionInitialized)
                return;

            _reflectionInitialized = true;
            Type managerType = Type.GetType(StoryModeManagerTypeName + ", StoryMode", throwOnError: false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(StoryModeManagerTypeName, throwOnError: false))
                    .FirstOrDefault(type => type != null);
            if (managerType == null)
                return;

            try
            {
                _currentProperty = managerType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                _mainStoryLineProperty = managerType.GetProperty("MainStoryLine", BindingFlags.Public | BindingFlags.Instance);
                Type mainStoryLineType = _mainStoryLineProperty?.PropertyType;
                _thirdPhaseProperty = mainStoryLineType?.GetProperty("ThirdPhase", BindingFlags.Public | BindingFlags.Instance);
                Type thirdPhaseType = _thirdPhaseProperty?.PropertyType;
                _oppositionKingdomsProperty = thirdPhaseType?.GetProperty("OppositionKingdoms", BindingFlags.Public | BindingFlags.Instance);
                _thirdPhaseCompletedProperty = thirdPhaseType?.GetProperty("IsCompleted", BindingFlags.Public | BindingFlags.Instance);

                if (_currentProperty == null
                    || _mainStoryLineProperty == null
                    || _thirdPhaseProperty == null
                    || _oppositionKingdomsProperty == null)
                {
                    LogReflectionFailureOnce("Story Mode's third-phase quest API did not match the expected shape", null);
                }
            }
            catch (Exception ex)
            {
                LogReflectionFailureOnce("could not initialize Story Mode quest reflection", ex);
            }
        }

        private static void LogReflectionFailureOnce(string message, Exception ex)
        {
            if (_reflectionFailureLogged)
                return;

            _reflectionFailureLogged = true;
            BellumCivileLogger.Log($"Storyline war protection disabled: {message}{(ex == null ? "." : $"; {ex.GetType().Name}: {ex.Message}")}");
        }
    }
}
