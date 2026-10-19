using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TopsailYards
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "fluffyjesus.sailwind.topsailyards";
        public const string NAME = "Topsail Yards";
        public const string VERSION = "1.0.7";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> DebugLog;

        private void Awake()
        {
            Log = Logger;
            DebugLog = Config.Bind(
                "Debug", "VerboseLog", false,
                "Писать в лог решение по каждому прямому парусу (почему рея добавлена / не добавлена).");

            new Harmony(GUID).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{NAME} {VERSION} loaded successfully!");
        }

        internal static bool IsExcludedSail(Sail sail)
        {
            if (sail == null) return true;
            if (sail.junkType) return true;

            string n = (sail.name + " " + sail.sailName).ToLowerInvariant();

            if (n.Contains("wide") && n.Contains("square")) return true;
            if (n.Contains("sprit")) return true;
            if (n.Contains("junk")) return true;

            return false;
        }
    }

    [HarmonyPatch(typeof(Mast), "TopsailCheckAndAttach")]
    internal static class TopsailCheckPatch
    {
        private static bool Prefix(Mast __instance, GameObject squareSail, int mastOrder, ref bool __result)
        {
            var existing = squareSail.GetComponent<SquareTopsailAngleMirror>();
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing);
            }

            // Ищем прямой парус ниже: сначала на этой же мачте (как в ваниле),
            // потом на другой мачте той же оси (мачта под топмачтой).
            bool crossMast;
            HingeJoint below = YardManager.FindTopsailBase(__instance, squareSail.GetComponent<Sail>(), mastOrder, out crossMast);

            if (below != null)
            {
                squareSail.AddComponent<SquareTopsailAngleMirror>().sailBelow = below;

                if (crossMast && Plugin.DebugLog.Value)
                {
                    Plugin.Log.LogInfo($"[Topsail] {squareSail.name} @ {__instance.name}: attached to sail below on another mast ({below.gameObject.name})");
                }

                __result = true;
                return false;
            }

            __result = false;
            return false;
        }
    }

    /// <summary>
    /// Ванильный SquareTopsailAngleMirror.Initialize берёт точку крепления нижнего паруса так:
    ///   нижний.angleController.RopeEffect.attachment.RopeEffect.attachment
    /// FootYard подменяет RopeEffect.attachment контроллера на конец своей реи (у неё нет RopeEffect),
    /// и тогда Initialize падает с NullReferenceException в каждом кадре: ready не выставляется,
    /// контроллеры верхнего паруса не переносятся и висят на своих местах.
    /// Здесь инициализация делается заново с запасным путём через оригинальную цель из FootYard.
    /// </summary>
    [HarmonyPatch(typeof(SquareTopsailAngleMirror), "LateUpdate")]
    internal static class TopsailMirrorInitPatch
    {
        private const BindingFlags AnyField = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo FReady = typeof(SquareTopsailAngleMirror).GetField("ready", AnyField);
        private static readonly FieldInfo FConnections = typeof(SquareTopsailAngleMirror).GetField("connections", AnyField);
        private static readonly FieldInfo FLeftRope = typeof(SquareTopsailAngleMirror).GetField("leftRope", AnyField);
        private static readonly FieldInfo FRightRope = typeof(SquareTopsailAngleMirror).GetField("rightRope", AnyField);
        private static readonly FieldInfo FLeftConnection = typeof(SquareTopsailAngleMirror).GetField("leftConnection", AnyField);
        private static readonly FieldInfo FRightConnection = typeof(SquareTopsailAngleMirror).GetField("rightConnection", AnyField);

        private static readonly FieldInfo FFootOrigLeft = typeof(FootYard).GetField("origRopeLTarget", AnyField);
        private static readonly FieldInfo FFootOrigRight = typeof(FootYard).GetField("origRopeRTarget", AnyField);

        private static readonly HashSet<int> warned = new HashSet<int>();

        private static bool Prefix(SquareTopsailAngleMirror __instance)
        {
            // Если рефлексия не нашла поля, не вмешиваемся
            if (FReady == null || FConnections == null || FLeftRope == null || FRightRope == null ||
                FLeftConnection == null || FRightConnection == null)
                return true;

            if ((bool)FReady.GetValue(__instance)) return true;

            try
            {
                if (TryInitialize(__instance)) return true;
            }
            catch (Exception e)
            {
                WarnOnce(__instance, "mirror init failed: " + e);
                return false;
            }

            // Не даём ванильному Initialize падать с NRE каждый кадр, повторим в следующем
            WarnOnce(__instance, "mirror init: can't resolve connection points of the sail below, will retry");
            return false;
        }

        private static bool TryInitialize(SquareTopsailAngleMirror m)
        {
            var connections = m.GetComponent<SailConnections>();
            if (connections == null || connections.angleControllerLeft == null || connections.angleControllerRight == null)
                return false;

            var leftRope = connections.angleControllerLeft.GetComponent<RopeEffect>();
            var rightRope = connections.angleControllerRight.GetComponent<RopeEffect>();
            if (leftRope == null || rightRope == null || m.sailBelow == null) return false;

            var below = m.sailBelow.GetComponent<SailConnections>();
            if (below == null) return false;

            var foot = m.sailBelow.GetComponent<FootYard>();

            Transform left = ResolveConnection(below.angleControllerLeft, foot, FFootOrigLeft);
            Transform right = ResolveConnection(below.angleControllerRight, foot, FFootOrigRight);
            if (left == null || right == null) return false;

            FConnections.SetValue(m, connections);
            FLeftRope.SetValue(m, leftRope);
            FRightRope.SetValue(m, rightRope);
            FLeftConnection.SetValue(m, left);
            FRightConnection.SetValue(m, right);
            FReady.SetValue(m, true);

            if (Plugin.DebugLog.Value)
            {
                Plugin.Log.LogInfo($"[Topsail] mirror ready: {m.name} -> {m.sailBelow.name} (FootYard below: {foot != null}), left={left.name}, right={right.name}");
            }
            return true;
        }

        private static Transform ResolveConnection(RopeController controller, FootYard foot, FieldInfo footOrigField)
        {
            if (controller == null) return null;

            var re = controller.GetComponent<RopeEffect>();
            if (re == null || re.attachment == null) return null;

            Transform x = re.attachment;
            RopeEffect xr = x.GetComponent<RopeEffect>();

            // FootYard подменил attachment на конец своей реи (без RopeEffect): берём оригинал, как в ваниле
            if (xr == null && foot != null && footOrigField != null)
            {
                var orig = footOrigField.GetValue(foot) as Transform;
                if (orig != null)
                {
                    x = orig;
                    xr = orig.GetComponent<RopeEffect>();
                }
            }

            if (xr == null || xr.attachment == null) return null;
            return xr.attachment;
        }

        private static void WarnOnce(SquareTopsailAngleMirror m, string msg)
        {
            if (!warned.Add(m.GetInstanceID())) return;
            Plugin.Log.LogWarning($"[Topsail] {m.name}: {msg}");
        }
    }

    [HarmonyPatch(typeof(Mast), "UpdateControllerAttachments")]
    internal static class MastPatch
    {
        private static void Postfix(Mast __instance)
        {
            try
            {
                YardManager.RefreshAll(__instance);
                YardManager.SyncCrossMastTopsails(__instance.shipRigidbody);
            }
            catch (Exception e) { Plugin.Log.LogError("YardManager.Refresh failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(Sail), "Start")]
    internal static class SailStartPatch
    {
        private static void Postfix(Sail __instance)
        {
            if (__instance.transform.parent != null)
            {
                var mast = __instance.transform.parent.GetComponent<Mast>();
                if (mast != null)
                {
                    try
                    {
                        YardManager.RefreshAll(mast);
                        // К моменту Sail.Start позиции парусов уже окончательные
                        YardManager.SyncCrossMastTopsails(mast.shipRigidbody);
                    }
                    catch (Exception e) { Plugin.Log.LogError("SailStart sync failed: " + e); }
                }
            }
        }
    }

    internal static class YardManager
    {
        /// <summary>
        /// Радиус (м) в плоскости палубы (оси X/Z корабля), в пределах которого паруса
        /// считаются стоящими на одной вертикальной оси: мачта + её топмачта.
        /// Паруса сидят на оси своей мачты (localPosition = (0, 0, z)), поэтому
        /// позиция паруса — это точка на оси мачты, и имена мачт не нужны.
        /// </summary>
        private const float StackRadius = 1.5f;

        /// <summary>Насколько ниже (м) должен быть другой парус, чтобы считаться «под».</summary>
        private const float MinVerticalGap = 0.25f;

        /// <summary>
        /// Ищет шарнир прямого паруса, к рее которого цепляется данный прямой парус (как ванильный топсель).
        /// Сначала как в ваниле: ближайший прямой парус ниже на ЭТОЙ ЖЕ мачте.
        /// Если такого нет: ближайший прямой парус ниже на ДРУГОЙ мачте той же оси
        /// (мачта под топмачтой). Ваниль так не умеет: там топмачта для него отдельная мачта,
        /// и канаты паруса уходят на лебёдки топмачты.
        /// </summary>
        internal static HingeJoint FindTopsailBase(Mast mast, Sail me, int mastOrder, out bool crossMast)
        {
            crossMast = false;
            if (mast == null || mast.sails == null) return null;

            // 1. Как в ваниле: прямой парус ниже на этой же мачте
            for (int i = mastOrder - 1; i >= 0; i--)
            {
                if (i >= mast.sails.Count) continue;
                var go = mast.sails[i];
                if (go == null || !go.activeInHierarchy) continue;

                var sail = go.GetComponent<Sail>();
                if (sail == null || sail.obsolete) continue;

                if (sail.squareSail) return go.GetComponent<HingeJoint>();
            }

            // 2. Парус на другой мачте той же оси. Спритсель, джонку и wide square не трогаем.
            if (me == null || mast.shipRigidbody == null) return null;
            if (Plugin.IsExcludedSail(me)) return null;

            var ship = mast.shipRigidbody.transform;
            Vector3 myLocal = ship.InverseTransformPoint(me.transform.position);

            HingeJoint best = null;
            float bestY = float.MinValue;

            foreach (var other in mast.shipRigidbody.GetComponentsInChildren<Mast>(true))
            {
                if (other == null || other == mast || other.sails == null) continue;

                foreach (var go in other.sails)
                {
                    if (go == null || !go.activeInHierarchy) continue;

                    var s = go.GetComponent<Sail>();
                    if (s == null || s == me || s.obsolete || !s.squareSail) continue;

                    Vector3 l = ship.InverseTransformPoint(s.transform.position);
                    if (l.y >= myLocal.y - MinVerticalGap) continue; // не ниже

                    float dx = l.x - myLocal.x;
                    float dz = l.z - myLocal.z;
                    if (Mathf.Sqrt(dx * dx + dz * dz) >= StackRadius) continue; // другая ось

                    var hinge = go.GetComponent<HingeJoint>();
                    if (hinge == null) continue;

                    if (l.y > bestY)
                    {
                        bestY = l.y;
                        best = hinge;
                    }
                }
            }

            crossMast = best != null;
            return best;
        }

        private static bool syncing;

        /// <summary>
        /// Ваниль пересчитывает привязку только у той мачты, на которой что-то менялось.
        /// Если мачта под топмачтой загрузилась позже топмачты (загрузка сохранения) или в ней убрали/добавили парус,
        /// парус на топмачте остаётся со старым решением. Здесь сверяем, что решил бы TopsailCheckAndAttach сейчас,
        /// с тем, что реально висит на парусе, и пересчитываем мачту, если не совпало.
        /// </summary>
        internal static void SyncCrossMastTopsails(Rigidbody shipRb)
        {
            if (syncing || shipRb == null) return;

            syncing = true;
            try
            {
                foreach (var mast in shipRb.GetComponentsInChildren<Mast>(true))
                {
                    if (mast == null || mast.sails == null) continue;

                    bool stale = false;
                    foreach (var go in mast.sails)
                    {
                        if (go == null || !go.activeInHierarchy) continue;

                        var s = go.GetComponent<Sail>();
                        if (s == null || s.obsolete || !s.squareSail) continue;

                        bool crossMast;
                        HingeJoint wanted = FindTopsailBase(mast, s, s.mastOrder, out crossMast);
                        var mirror = s.GetComponent<SquareTopsailAngleMirror>();

                        bool hasMirror = mirror != null;
                        bool wantMirror = wanted != null;
                        if (hasMirror != wantMirror || (hasMirror && mirror.sailBelow != wanted))
                        {
                            stale = true;
                            break;
                        }
                    }

                    // Внутри вызова сработает Postfix мачты и обновит реи; повторный вход закрыт флагом
                    if (stale) mast.UpdateControllerAttachments();
                }
            }
            finally
            {
                syncing = false;
            }
        }

        private struct SailInfo
        {
            public Sail sail;
            public Mast mast;
            public Vector3 local; // позиция в системе координат корабля
        }

        public static void RefreshAll(Mast sourceMast)
        {
            if (sourceMast == null || sourceMast.shipRigidbody == null) return;

            var ship = sourceMast.shipRigidbody.transform;
            var allMasts = sourceMast.shipRigidbody.GetComponentsInChildren<Mast>(true);

            // 1. Собираем ВСЕ активные паруса корабля (мачты + топмачты) в один список
            var all = new List<SailInfo>();
            foreach (var mast in allMasts)
            {
                if (mast == null || mast.sails == null) continue;
                foreach (var go in mast.sails)
                {
                    if (go == null || !go.activeInHierarchy) continue;
                    var s = go.GetComponent<Sail>();
                    if (s == null || s.obsolete) continue;

                    all.Add(new SailInfo
                    {
                        sail = s,
                        mast = mast,
                        local = ship.InverseTransformPoint(s.transform.position)
                    });
                }
            }

            // 2. Каждый парус оцениваем РОВНО ОДИН РАЗ, относительно паруса на его оси
            for (int i = 0; i < all.Count; i++)
            {
                var sail = all[i].sail;

                string reason;
                bool needYard = IsEligible(all, i, out reason);

                if (Plugin.DebugLog.Value && IsAnySquareSail(sail))
                {
                    Plugin.Log.LogInfo(
                        $"[Yard] {sail.name} @ {all[i].mast.name} y={all[i].local.y:F2} -> " +
                        (needYard ? "YARD" : "no yard") + ": " + reason);
                }

                var existingYard = sail.GetComponent<FootYard>();
                if (needYard && existingYard == null)
                {
                    sail.gameObject.AddComponent<FootYard>();
                }
                else if (!needYard && existingYard != null)
                {
                    UnityEngine.Object.Destroy(existingYard);
                }
            }
        }

        /// <summary>
        /// Два паруса на одной оси: либо на одной мачте, либо рядом по горизонтали
        /// (мачта и топмачта над ней).
        /// </summary>
        private static bool SameStack(SailInfo a, SailInfo b)
        {
            if (a.mast == b.mast) return true;
            return HorizontalDistance(a, b) < StackRadius;
        }

        private static float HorizontalDistance(SailInfo a, SailInfo b)
        {
            float dx = a.local.x - b.local.x;
            float dz = a.local.z - b.local.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static bool IsAnySquareSail(Sail sail)
        {
            if (sail == null) return false;

            if (sail.category == SailCategory.staysail ||
                sail.category == SailCategory.lateen ||
                sail.category == SailCategory.gaff) return false;

            if (sail.squareSail) return true;
            if (sail.category == SailCategory.square) return true;

            string n = (sail.name + " " + sail.sailName).ToLowerInvariant();
            if (n.Contains("jib") || n.Contains("staysail") || n.Contains("lateen") ||
                n.Contains("gaff") || n.Contains("junk")) return false;

            return n.Contains("square") || n.Contains("trapezoid") || n.Contains("cog") || n.Contains("topsail") || n.Contains("sprit");
        }

        /// <summary>
        /// Рея нужна, если под прямым парусом (на этой же оси, на мачте или топмачте) есть паруса
        /// и среди них НЕТ прямого паруса на ТОЙ ЖЕ мачте (только его ваниль цепляет к своей рее).
        /// Прямой парус на другой мачте (топмачта над мачтой) рею не отменяет.
        /// </summary>
        private static bool IsEligible(List<SailInfo> all, int index, out string reason)
        {
            var me = all[index];
            var sail = me.sail;

            // 1. Исключённые типы (Wide Square, Spritsail, Junk)
            if (Plugin.IsExcludedSail(sail)) { reason = "excluded type"; return false; }

            // 2. Только прямые паруса
            if (!IsAnySquareSail(sail)) { reason = "not a square sail"; return false; }

            // 3. Ванильный топсель (уже цепляется к рее нижнего паруса на этой же мачте)
            if (sail.GetComponent<SquareTopsailAngleMirror>() != null)
            {
                reason = "vanilla topsail mirror";
                return false;
            }

            // 4. Смотрим на всё, что ниже на той же оси
            bool anyBelow = false;
            string crossMastSquare = null;         // прямой парус ниже, но на другой мачте
            float nearestOffAxis = float.MaxValue; // только для диагностики

            for (int j = 0; j < all.Count; j++)
            {
                if (j == index) continue;
                var other = all[j];

                if (other.local.y >= me.local.y - MinVerticalGap) continue; // не ниже

                if (!SameStack(me, other))
                {
                    float d = HorizontalDistance(me, other);
                    if (d < nearestOffAxis) nearestOffAxis = d;
                    continue;
                }

                anyBelow = true;

                if (IsAnySquareSail(other.sail))
                {
                    // Ваниль цепляет парус к рее нижнего прямого паруса ТОЛЬКО в пределах одной мачты
                    // (Mast.TopsailCheckAndAttach смотрит лишь в mast.sails). Прямой парус на ДРУГОЙ мачте
                    // (мачта под топмачтой) ваниль не подхватывает: канаты уходят на лебёдки топмачты.
                    // Поэтому такой парус не блокирует рею.
                    if (other.mast == me.mast)
                    {
                        reason = "square sail below on the same mast: " + other.sail.name + " @ " + other.mast.name;
                        return false;
                    }

                    if (crossMastSquare == null)
                        crossMastSquare = other.sail.name + " @ " + other.mast.name;
                }
            }

            // Под парусом ничего нет — он над палубой, рея не нужна
            if (!anyBelow)
            {
                reason = "nothing below on this axis";
                if (nearestOffAxis < float.MaxValue)
                    reason += $" (nearest lower sail is off-axis by {nearestOffAxis:F2} m, radius = {StackRadius} m)";
                return false;
            }

            // Снизу есть паруса, и ни одного прямого на этой же мачте — рея нужна
            reason = crossMastSquare != null
                ? "square sail below only on another mast (" + crossMastSquare + "), vanilla won't attach -> yard needed"
                : "only non-square sails below";
            return true;
        }
    }
}