using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TopsailYards
{
    /// Writes the structure of a sail to the log, so the yard placement can be tuned from facts, not guesses.
    internal static class Dump
    {
        public static void Describe(Sail sail, string note, int vertexCount, int footCount,
                                Vector3 cornerA, Vector3 cornerB, float height, bool pinned)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Topsail yard: sail '" + sail.name + "' ===");
            sb.AppendLine("category=" + sail.category + " squareSail=" + sail.squareSail + " junk=" + sail.junkType +
                          " mastOrder=" + sail.mastOrder + " installHeight=" + sail.installHeight.ToString("0.##") +
                          " area=" + sail.sailArea.ToString("0.#") + " unroll=" + sail.currentUnroll.ToString("0.##"));

            var mast = sail.transform.parent != null ? sail.transform.parent.GetComponent<Mast>() : null;
            if (mast != null && mast.sails != null)
            {
                sb.Append("mast '" + mast.name + "', sails bottom to top:");
                foreach (var go in mast.sails)
                {
                    if (go == null) continue;
                    var s = go.GetComponent<Sail>();
                    sb.Append(" [" + go.name + ":" + (s != null ? s.category.ToString() : "?") + "]");
                }
                sb.AppendLine();
            }

            sb.AppendLine("cloth vertices=" + vertexCount + ", foot vertices=" + footCount + ", pinned=" + pinned +
                          ", skinned height=" + height.ToString("0.00") + " m");
            if (!string.IsNullOrEmpty(note)) sb.AppendLine(note);
            sb.AppendLine("foot corners (world): " + cornerA.ToString("F2") + " .. " + cornerB.ToString("F2") +
                          ", width=" + (cornerA - cornerB).magnitude.ToString("0.00") + " m");

            var sc = sail.GetComponent<SailConnections>();
            if (sc != null)
            {
                Rope(sb, "angleControllerLeft", sc.angleControllerLeft, cornerA, cornerB);
                Rope(sb, "angleControllerRight", sc.angleControllerRight, cornerA, cornerB);
                Rope(sb, "angleControllerMid", sc.angleControllerMid, cornerA, cornerB);
                Rope(sb, "midRopeAttachment", sc.midRopeAttachment, cornerA, cornerB);
            }

            var smr = sail.cloth != null ? sail.cloth.GetComponent<SkinnedMeshRenderer>() : null;
            if (smr != null && smr.bones != null)
            {
                sb.AppendLine("cloth bones (" + smr.bones.Length + "), first 24:");
                for (int i = 0; i < smr.bones.Length && i < 24; i++)
                    if (smr.bones[i] != null)
                        sb.AppendLine("  " + smr.bones[i].name + " " + smr.bones[i].position.ToString("F2"));
            }

            sb.AppendLine("hierarchy:");
            Tree(sb, sail.transform, 0);
            Plugin.Log.LogInfo(sb.ToString());
        }

        /// Everything the game has, without any assumptions: all sails and the hierarchy of the square ones.
        public static void Overview()
        {
            var all = UnityEngine.Object.FindObjectsOfType<Sail>();
            var sb = new StringBuilder();
            sb.AppendLine("=== Sail overview (" + all.Length + " active sails) ===");
            foreach (var s in all)
            {
                var mast = s.transform.parent != null ? s.transform.parent.GetComponent<Mast>() : null;
                sb.AppendLine("'" + s.name + "' category=" + s.category + " squareSail=" + s.squareSail + " junk=" + s.junkType +
                              " mastOrder=" + s.mastOrder + " installed=" + s.IsInstalled() +
                              " unroll=" + s.currentUnroll.ToString("0.##") + " mast=" + (mast != null ? mast.name : "-") +
                              " yard=" + (s.GetComponent<FootYard>() != null));
            }
            Plugin.Log.LogInfo(sb.ToString());

            foreach (var s in all)
            {
                if (s.category != SailCategory.square) continue;
                var t = new StringBuilder();
                t.AppendLine("--- hierarchy of '" + s.name + "' ---");
                Tree(t, s.transform, 0);
                Plugin.Log.LogInfo(t.ToString());
            }
        }

        private static void Rope(StringBuilder sb, string label, Component c, Vector3 a, Vector3 b)
        {
            if (c == null) { sb.AppendLine(label + ": none"); return; }
            Vector3 p = c.transform.position;
            sb.AppendLine(label + ": '" + c.name + "' at " + p.ToString("F2") +
                          ", distance to foot corners " + Vector3.Distance(p, a).ToString("0.00") + " / " +
                          Vector3.Distance(p, b).ToString("0.00") + " m");
        }

        private static void Tree(StringBuilder sb, Transform t, int depth)
        {
            if (depth > 4) return;
            var names = new List<string>();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                names.Add(c.GetType().Name);
            }
            string line = new string(' ', depth * 2 + 2) + t.name + (t.gameObject.activeSelf ? "" : " (inactive)") +
                          "  [" + string.Join(", ", names.ToArray()) + "]";

            var r = t.GetComponent<Renderer>();
            if (r != null)
            {
                var mn = new List<string>();
                foreach (var m in r.sharedMaterials)
                    mn.Add(m != null ? m.name + "/" + (m.shader != null ? m.shader.name : "?") : "null");
                line += "  size " + r.bounds.size.ToString("F2") + "  mats: " + string.Join(", ", mn.ToArray());

                var smr = r as SkinnedMeshRenderer;
                if (smr != null)
                    line += "  SMR bones=" + (smr.bones != null ? smr.bones.Length : 0) +
                            " mesh=" + (smr.sharedMesh != null ? smr.sharedMesh.name + " v=" + smr.sharedMesh.vertexCount : "null");
            }
            sb.AppendLine(line);
            for (int i = 0; i < t.childCount; i++) Tree(sb, t.GetChild(i), depth + 1);
        }
    }
}