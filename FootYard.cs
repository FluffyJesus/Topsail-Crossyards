using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TopsailYards
{
    internal sealed class FootYard : MonoBehaviour
    {
        private const float YardRadius = 0.055f;
        private const float YardOverhang = 0.35f;
        private const float ThicknessRatio = 1.06f;
        private const float LengthScale = 1.075f;
        private static readonly Color FallbackWoodColor = new Color(0.243f, 0.173f, 0.125f);

        private Sail sail;
        private SailConnections sc;

        private Transform yard;
        private Transform sparVisual;
        private Transform bracketVisual;
        private MeshRenderer yardRenderer;
        private Transform topYardTransform;
        private Renderer topYardRenderer;

        // Ноки рея (концы с блочками)
        private Transform leftYardArm;
        private Transform rightYardArm;

        // Ванильные шкоты с палубы
        private RopeEffect deckRopeL;
        private RopeEffect deckRopeR;
        private Transform origRopeLTarget;
        private Transform origRopeRTarget;

        // Настоящие игровые верёвки-шкоты с блоками (clothRopeSheetPrefab)
        private RopeEffect leftFootRope;
        private RopeEffect rightFootRope;

        // Топенанты к блоку на мачте
        private Transform liftTargetAnchor;
        private RopeEffect leftLiftRope;
        private RopeEffect rightLiftRope;

        private float yardLength = 10.5f;
        private float lastScaleY = -1f;
        private float lastScaleZ = -1f;
        private Vector3 dominantAxis = Vector3.right;
        private bool initialized;

        private void Awake()
        {
            sail = GetComponent<Sail>();
            sc = GetComponent<SailConnections>();
        }

        private void LateUpdate()
        {
            if (sail == null || !sail.IsInstalled() || sail.shipRigidbody == null)
                return;

            if (!initialized)
            {
                TryInit();
                return;
            }

            // Адаптация положения ТОЛЬКО при масштабировании паруса на верфи
            float curScaleY = sail.GetScaleY();
            float curScaleZ = sail.GetScaleZ();
            if (Mathf.Abs(curScaleY - lastScaleY) > 0.005f || Mathf.Abs(curScaleZ - lastScaleZ) > 0.005f)
            {
                lastScaleY = curScaleY;
                lastScaleZ = curScaleZ;
                ApplyStaticTransform(curScaleY, curScaleZ);
            }

            UpdateRigging();
        }

        private void TryInit()
        {
            topYardTransform = FindTopYard(out topYardRenderer, out yardLength);
            if (topYardTransform == null) return;

            // 1. Создаем корневой контейнер рея
            yard = new GameObject("TopsailFootYard").transform;
            yard.SetParent(transform, false);
            yard.gameObject.layer = gameObject.layer;
            yard.localScale = Vector3.one;

            // 2. Создаем чистый визуальный объект бруса
            var sparObj = new GameObject("SparVisual");
            sparObj.transform.SetParent(yard, false);
            sparObj.transform.localPosition = Vector3.zero;
            sparObj.transform.localRotation = Quaternion.identity;
            sparVisual = sparObj.transform;

            // 3. Берём чистый меш оригинального рея
            var mf = sparObj.AddComponent<MeshFilter>();
            Mesh topMesh = MeshOf(topYardTransform);

            if (topMesh != null)
            {
                mf.sharedMesh = topMesh;
                // ИСПРАВЛЕНИЕ БАГА 1: Вычисляем ось строго по поперечной оси корабля!
                dominantAxis = GetMeshDominantAxis(topYardTransform, topMesh);
            }
            else
            {
                mf.sharedMesh = YardMesh.Get();
                dominantAxis = Vector3.right;
            }

            // 4. Копируем родные материалы верхнего рея
            yardRenderer = sparObj.AddComponent<MeshRenderer>();
            yardRenderer.sharedMaterials = MakeProperWoodMaterials(topYardRenderer);
            CopyPropertyBlock(topYardRenderer, yardRenderer);
            yardRenderer.shadowCastingMode = ShadowCastingMode.On;
            yardRenderer.receiveShadows = true;

            // 5. Клонируем кронштейн к мачте
            FindAndCloneBracket();

            // 6. Создаем ноки рея
            leftYardArm = CreateYardArm("YardArm_L");
            rightYardArm = CreateYardArm("YardArm_R");

            // 7. Перехватываем шкоты с палубы на ноки реи
            HookDeckWinches();

            // 8. Создаем ванильные шкоты с блоками (clothRopeSheetPrefab)
            if (leftYardArm != null && origRopeLTarget != null)
                leftFootRope = CreateSailSheetRope("LeftFootRope", leftYardArm, origRopeLTarget);

            if (rightYardArm != null && origRopeRTarget != null)
                rightFootRope = CreateSailSheetRope("RightFootRope", rightYardArm, origRopeRTarget);

            // 9. Создаем топенанты к блоку на мачте
            liftTargetAnchor = FindMastBlockAnchor();
            if (liftTargetAnchor == null)
            {
                var fallbackAnchor = new GameObject("LiftMastAnchorFallback").transform;
                fallbackAnchor.SetParent(transform, false);
                Vector3 topCenterWorld = topYardTransform.position;
                Mesh topM = MeshOf(topYardTransform);
                if (topM != null) topCenterWorld = topYardTransform.TransformPoint(topM.bounds.center);
                Vector3 topCenterLocal = transform.InverseTransformPoint(topCenterWorld);

                fallbackAnchor.localPosition = new Vector3(0f, 0f, topCenterLocal.z + 0.15f);
                liftTargetAnchor = fallbackAnchor;
            }

            if (leftYardArm != null && liftTargetAnchor != null)
                leftLiftRope = CreateLiftRope("LeftLiftRope", leftYardArm, liftTargetAnchor);

            if (rightYardArm != null && liftTargetAnchor != null)
                rightLiftRope = CreateLiftRope("RightLiftRope", rightYardArm, liftTargetAnchor);

            lastScaleY = sail.GetScaleY();
            lastScaleZ = sail.GetScaleZ();
            ApplyStaticTransform(lastScaleY, lastScaleZ);

            initialized = true;
        }

        private Transform FindMastBlockAnchor()
        {
            if (transform.parent != null)
            {
                var mast = transform.parent.GetComponent<Mast>();
                if (mast != null && sail != null)
                {
                    int order = sail.mastOrder;

                    if (mast.mastReefAtt != null && mast.mastReefAtt.Length > order && mast.mastReefAtt[order] != null)
                        return mast.mastReefAtt[order];

                    if (mast.mastReefAttExtension != null && mast.mastReefAttExtension.Length > order && mast.mastReefAttExtension[order] != null)
                        return mast.mastReefAttExtension[order];

                    if (mast.midRopeAtt != null && mast.midRopeAtt.Length > order && mast.midRopeAtt[order] != null)
                        return mast.midRopeAtt[order];
                }
            }

            if (sc != null)
            {
                if (sc.mastReefAttachment != null) return sc.mastReefAttachment;
                if (sc.mastReefAttExtension != null) return sc.mastReefAttExtension;
                if (sc.midRopeAttachment != null) return sc.midRopeAttachment;
            }

            return null;
        }

        private void FindAndCloneBracket()
        {
            Renderer bracketRend = null;
            Transform bracketTrans = null;

            if (topYardTransform != null)
            {
                foreach (var r in topYardTransform.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == topYardRenderer) continue;
                    if (IsLikelyBracket(r))
                    {
                        bracketRend = r;
                        bracketTrans = r.transform;
                        break;
                    }
                }
            }

            if (bracketRend == null)
            {
                Vector3 up = sail.shipRigidbody != null ? sail.shipRigidbody.transform.up : Vector3.up;
                float topH = Vector3.Dot(topYardTransform.position - transform.position, up);

                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r == topYardRenderer || (yard != null && r.transform.IsChildOf(yard))) continue;
                    if (!r.enabled) continue;

                    float h = Vector3.Dot(r.transform.position - transform.position, up);
                    if (Mathf.Abs(h - topH) < 0.5f && IsLikelyBracket(r))
                    {
                        bracketRend = r;
                        bracketTrans = r.transform;
                        break;
                    }
                }
            }

            if (bracketRend == null || bracketTrans == null) return;

            Mesh bMesh = MeshOf(bracketTrans);
            if (bMesh == null) return;

            var bObj = new GameObject("BracketVisual");
            bObj.transform.SetParent(yard, false);
            bracketVisual = bObj.transform;

            bracketVisual.localPosition = topYardTransform.InverseTransformPoint(bracketTrans.position);
            bracketVisual.localRotation = Quaternion.Inverse(topYardTransform.rotation) * bracketTrans.rotation;
            bracketVisual.localScale = bracketTrans.localScale;

            var bmf = bObj.AddComponent<MeshFilter>();
            bmf.sharedMesh = bMesh;

            var bmr = bObj.AddComponent<MeshRenderer>();
            bmr.sharedMaterials = bracketRend.sharedMaterials;
            CopyPropertyBlock(bracketRend, bmr);
            bmr.shadowCastingMode = ShadowCastingMode.On;
            bmr.receiveShadows = true;
        }

        private bool IsLikelyBracket(Renderer r)
        {
            if (r == null || r is SkinnedMeshRenderer) return false;
            Mesh m = MeshOf(r.transform);
            if (m == null) return false;

            string nm = (r.name + " " + (r.transform.parent != null ? r.transform.parent.name : "")).ToLowerInvariant();
            if (nm.Contains("col") || nm.Contains("shadow") || nm.Contains("rope") || nm.Contains("cloth") || nm.Contains("furled"))
                return false;

            Vector3 s = m.bounds.size;
            Vector3 lossy = r.transform.lossyScale;
            float maxDim = Mathf.Max(s.x * Mathf.Abs(lossy.x), Mathf.Max(s.y * Mathf.Abs(lossy.y), s.z * Mathf.Abs(lossy.z)));

            return maxDim >= 0.15f && maxDim <= 0.85f;
        }

        private static Mesh MeshOf(Transform t)
        {
            if (t == null) return null;
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
            var smr = t.GetComponent<SkinnedMeshRenderer>();
            return smr != null ? smr.sharedMesh : null;
        }

        private struct YardCandidate
        {
            public Transform t;
            public Renderer r;
            public float dim;
            public float height;
            public bool named;
        }

        private Transform FindTopYard(out Renderer topRend, out float calculatedWidth)
        {
            topRend = null;
            calculatedWidth = 10.5f;

            Renderer clothRend = sail.cloth != null ? sail.cloth.GetComponent<Renderer>() : null;
            var reefAnim = sail.GetComponent<ReefEffectAnimUniversal>();
            Renderer furledRend = reefAnim != null ? reefAnim.furledSail : null;
            Vector3 up = sail.shipRigidbody != null ? sail.shipRigidbody.transform.up : Vector3.up;

            var list = new List<YardCandidate>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r == clothRend || r == furledRend) continue;
                if (yard != null && r.transform.IsChildOf(yard)) continue;
                if (!r.enabled) continue;

                Mesh mesh = null;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) mesh = smr.sharedMesh;
                else
                {
                    var mfr = r.GetComponent<MeshFilter>();
                    if (mfr != null) mesh = mfr.sharedMesh;
                }
                if (mesh == null) continue;

                string nm = (r.name + " " + (r.transform.parent != null ? r.transform.parent.name : "")).ToLowerInvariant();
                if (nm.Contains("col_checker") || nm.Contains("shadow") || nm.Contains("rope") ||
                    nm.Contains("cloth") || nm.Contains("canvas") || nm.Contains("furled"))
                    continue;

                Vector3 s = mesh.bounds.size;
                Vector3 lossy = r.transform.lossyScale;
                float dim = Mathf.Max(s.x * Mathf.Abs(lossy.x), Mathf.Max(s.y * Mathf.Abs(lossy.y), s.z * Mathf.Abs(lossy.z)));
                if (dim <= 0.8f) continue;

                Vector3 centerWorld = r.transform.TransformPoint(mesh.bounds.center);
                var c = new YardCandidate();
                c.t = r.transform;
                c.r = r;
                c.dim = dim;
                c.height = Vector3.Dot(centerWorld - transform.position, up);
                c.named = nm.Contains("yard") || nm.Contains("spar") || nm.Contains("boom");
                list.Add(c);
            }

            if (list.Count == 0)
            {
                calculatedWidth = 8f;
                return transform;
            }

            list.Sort((x, y) => y.height.CompareTo(x.height));

            float maxH = list[0].height;
            YardCandidate best = list[0];
            foreach (var c in list)
            {
                if (c.height < maxH - 0.25f) continue;
                if ((c.named && !best.named) || (c.named == best.named && c.dim > best.dim)) best = c;
            }

            topRend = best.r;
            calculatedWidth = best.dim;
            return best.t;
        }

        /// ИСПРАВЛЕНИЕ БАГА 1: Находит ось длины реи, ориентируясь строго на поперечную ось корабля
        private Vector3 GetMeshDominantAxis(Transform t, Mesh mesh)
        {
            if (t == null || mesh == null) return Vector3.right;
            Vector3 beam = sail.shipRigidbody != null ? sail.shipRigidbody.transform.right : transform.right;
            Vector3 localBeam = t.InverseTransformDirection(beam);
            
            float dx = Mathf.Abs(localBeam.x);
            float dy = Mathf.Abs(localBeam.y);
            float dz = Mathf.Abs(localBeam.z);

            if (dz >= dx && dz >= dy) return Vector3.forward;
            if (dy >= dx && dy >= dz) return Vector3.up;
            return Vector3.right;
        }

        private Material[] MakeProperWoodMaterials(Renderer topRend)
        {
            if (topRend != null && topRend.sharedMaterials != null && topRend.sharedMaterials.Length > 0)
            {
                var mats = new Material[topRend.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    Material src = topRend.sharedMaterials[i];
                    mats[i] = src != null ? new Material(src) : null;
                }
                return mats;
            }

            if (transform.parent != null)
            {
                foreach (var mr in transform.parent.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null)
                    {
                        string n = mr.sharedMaterial.name.ToLowerInvariant();
                        if (n.Contains("wood") || n.Contains("mast") || n.Contains("boat") || n.Contains("spar"))
                        {
                            return new Material[] { new Material(mr.sharedMaterial) };
                        }
                    }
                }
            }

            var fallbackMat = new Material(Shader.Find("Diffuse") ?? Shader.Find("Standard"));
            fallbackMat.color = FallbackWoodColor;
            return new Material[] { fallbackMat };
        }

        private static void CopyPropertyBlock(Renderer from, Renderer to)
        {
            if (from == null || to == null) return;
            var mpb = new MaterialPropertyBlock();
            from.GetPropertyBlock(mpb);
            to.SetPropertyBlock(mpb);
        }

        private float MeasureTopYardLength()
        {
            if (topYardTransform == null || topYardTransform == transform) return yardLength;
            Mesh topMeshM = MeshOf(topYardTransform);
            if (topMeshM == null) return yardLength;

            Vector3 s = topMeshM.bounds.size;
            Vector3 lossy = topYardTransform.lossyScale;
            float lx = s.x * Mathf.Abs(lossy.x), ly = s.y * Mathf.Abs(lossy.y), lz = s.z * Mathf.Abs(lossy.z);
            return Mathf.Max(lx, Mathf.Max(ly, lz));
        }

        private float GetSailLengthMultiplier()
        {
            string sName = (sail != null && !string.IsNullOrEmpty(sail.sailName)) ? sail.sailName : "";
            string goName = gameObject != null ? gameObject.name : "";
            string fullName = (sName + " " + goName).ToLowerInvariant();

            // 1. Brig topsail: +10% ширины
            if (fullName.Contains("brig") && fullName.Contains("topsail"))
                return 1.10f;

            // 2. Wide Square: -15% ширины
            if (fullName.Contains("wide") && fullName.Contains("square"))
                return 0.85f;

            // 3. Cog Trapezoid: +15% ширины
            if (fullName.Contains("trapezoid") || (fullName.Contains("cog") && fullName.Contains("trap")))
                return 1.15f;

            return 1.0f;
        }

        private Transform CreateYardArm(string name)
        {
            var arm = new GameObject(name).transform;
            arm.SetParent(yard, false);
            arm.localScale = Vector3.one;
            return arm;
        }

        private void HookDeckWinches()
        {
            if (sc == null) return;

            if (sc.angleControllerLeft != null)
            {
                deckRopeL = sc.angleControllerLeft.GetComponentInChildren<RopeEffect>();
                if (deckRopeL != null && leftYardArm != null)
                {
                    if (origRopeLTarget == null) origRopeLTarget = deckRopeL.attachment;
                    deckRopeL.attachment = leftYardArm;
                }
            }

            if (sc.angleControllerRight != null)
            {
                deckRopeR = sc.angleControllerRight.GetComponentInChildren<RopeEffect>();
                if (deckRopeR != null && rightYardArm != null)
                {
                    if (origRopeRTarget == null) origRopeRTarget = deckRopeR.attachment;
                    deckRopeR.attachment = rightYardArm;
                }
            }
        }

        private RopeEffect CreateSailSheetRope(string name, Transform fromArm, Transform targetClew)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fromArm, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            go.SetActive(false);

            var lr = go.AddComponent<LineRenderer>();
            if (deckRopeL != null)
            {
                var srcLr = deckRopeL.GetComponent<LineRenderer>();
                if (srcLr != null)
                {
                    lr.sharedMaterial = srcLr.sharedMaterial;
                    lr.shadowCastingMode = srcLr.shadowCastingMode;
                    lr.receiveShadows = srcLr.receiveShadows;
                }
            }

            var rope = go.AddComponent<RopeEffect>();
            rope.attachment = targetClew;
            rope.sheet = true;
            rope.ropeWidth = (deckRopeL != null) ? deckRopeL.ropeWidth : 0.038f;
            rope.totalRopeLength = 0.5f;
            rope.currentRopeLength = 0f;

            rope.ReceiveSailArea(sail.sailArea);

            go.SetActive(true);

            return rope;
        }

        private RopeEffect CreateLiftRope(string name, Transform fromArm, Transform targetAnchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fromArm, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            go.SetActive(false);

            var lr = go.AddComponent<LineRenderer>();
            if (deckRopeL != null)
            {
                var srcLr = deckRopeL.GetComponent<LineRenderer>();
                if (srcLr != null)
                {
                    lr.sharedMaterial = srcLr.sharedMaterial;
                    lr.shadowCastingMode = srcLr.shadowCastingMode;
                    lr.receiveShadows = srcLr.receiveShadows;
                }
            }

            var rope = go.AddComponent<RopeEffect>();
            rope.attachment = targetAnchor;
            rope.sheet = false;
            rope.ropeWidth = (deckRopeL != null) ? deckRopeL.ropeWidth * 0.85f : 0.03f;
            rope.totalRopeLength = 0.95f;
            rope.currentRopeLength = 0f;

            go.SetActive(true);

            return rope;
        }

        private static float Safe(float v) { return Mathf.Max(1e-3f, Mathf.Abs(v)); }

        private void ApplyStaticTransform(float scaleY, float scaleZ)
        {
            if (yard == null) return;

            bool haveTop = topYardTransform != null && topYardTransform != transform;

            Vector3 topCenterWorld = haveTop ? topYardTransform.position : transform.position;
            Mesh topMeshC = haveTop ? MeshOf(topYardTransform) : null;
            if (topMeshC != null)
                topCenterWorld = topYardTransform.TransformPoint(topMeshC.bounds.center);

            // ИСПРАВЛЕНИЕ БАГА 2: Вычисляем положение в мировых координатах корабля!
            // Рея ВСЕГДА будет строго на 28 см ниже паруса вдоль мачты, независимо от локальных осей префаба
            Vector3 mastUp = sail.shipRigidbody != null ? sail.shipRigidbody.transform.up : transform.up;
            float sailHeight = sail.GetScaledHeight();
            if (sailHeight < 0.5f) sailHeight = 3.5f * scaleY;
            float totalDrop = sailHeight + 0.28f;

            Vector3 targetYardWorld = topCenterWorld - (mastUp * totalDrop);
            yard.localPosition = transform.InverseTransformPoint(targetYardWorld);

            if (haveTop)
            {
                yard.rotation = topYardTransform.rotation;
            }
            else
            {
                Vector3 beamAxis = sail.shipRigidbody != null ? sail.shipRigidbody.transform.right : transform.right;
                Vector3 yardAxis = sail.transform.up;

                float dotUp = Mathf.Abs(Vector3.Dot(sail.transform.up, beamAxis));
                float dotRight = Mathf.Abs(Vector3.Dot(sail.transform.right, beamAxis));
                float dotFwd = Mathf.Abs(Vector3.Dot(sail.transform.forward, beamAxis));

                if (dotUp >= dotRight && dotUp >= dotFwd) yardAxis = sail.transform.up;
                else if (dotRight >= dotUp && dotRight >= dotFwd) yardAxis = sail.transform.right;
                else yardAxis = sail.transform.forward;

                yard.rotation = Quaternion.FromToRotation(Vector3.right, yardAxis.normalized);
            }

            // Масштабирование бруса
            float sailMultiplier = GetSailLengthMultiplier();
            float totalLength = (MeasureTopYardLength() + YardOverhang * 2f) * LengthScale * sailMultiplier;

            yard.localScale = Vector3.one;
            Vector3 ps = yard.lossyScale;
            float psAxis = Safe(dominantAxis == Vector3.right ? ps.x : dominantAxis == Vector3.forward ? ps.z : ps.y);

            Vector3 sparBasePos = Vector3.zero;

            if (sparVisual != null)
            {
                sparVisual.localRotation = Quaternion.identity;

                var mf = sparVisual.GetComponent<MeshFilter>();
                Mesh mesh = (mf != null) ? mf.sharedMesh : null;
                Vector3 meshSize = mesh != null ? mesh.bounds.size : Vector3.right;
                Vector3 meshCenter = mesh != null ? mesh.bounds.center : Vector3.zero;
                float baseMeshWidth = (dominantAxis == Vector3.right) ? meshSize.x :
                                      (dominantAxis == Vector3.forward) ? meshSize.z : meshSize.y;

                Vector3 tl = haveTop ? topYardTransform.lossyScale : Vector3.one;
                float thick = haveTop ? ThicknessRatio : YardRadius;

                float sx = thick * Safe(tl.x) / Safe(ps.x);
                float sy = thick * Safe(tl.y) / Safe(ps.y);
                float sz = thick * Safe(tl.z) / Safe(ps.z);
                float lengthRatio = totalLength / (Mathf.Max(0.1f, baseMeshWidth) * psAxis);

                if (dominantAxis == Vector3.right) sx = lengthRatio;
                else if (dominantAxis == Vector3.forward) sz = lengthRatio;
                else sy = lengthRatio;

                sparVisual.localScale = new Vector3(sx, sy, sz);

                sparBasePos = -Vector3.Scale(meshCenter, sparVisual.localScale);
                sparVisual.localPosition = sparBasePos;
            }

            // Ноки рея строго соосны с деревянным брусом (без зазоров сзади)
            float halfSpan = totalLength * 0.48f / psAxis;
            if (leftYardArm != null) leftYardArm.localPosition = sparBasePos - dominantAxis * halfSpan;
            if (rightYardArm != null) rightYardArm.localPosition = sparBasePos + dominantAxis * halfSpan;

            if (yardRenderer != null) yardRenderer.enabled = true;
        }

        private void UpdateRigging()
        {
            if (deckRopeL != null && deckRopeL.attachment != leftYardArm)
                deckRopeL.attachment = leftYardArm;
            if (deckRopeR != null && deckRopeR.attachment != rightYardArm)
                deckRopeR.attachment = rightYardArm;

            // Верёвки и блоки шкотов к углам паруса
            if (leftFootRope != null)
            {
                if (origRopeLTarget != null && leftFootRope.attachment != origRopeLTarget)
                    leftFootRope.attachment = origRopeLTarget;
                leftFootRope.currentRopeLength = 0f;
            }

            if (rightFootRope != null)
            {
                if (origRopeRTarget != null && rightFootRope.attachment != origRopeRTarget)
                    rightFootRope.attachment = origRopeRTarget;
                rightFootRope.currentRopeLength = 0f;
            }

            // Топенанты к блоку на мачте
            if (leftLiftRope != null)
            {
                if (liftTargetAnchor != null && leftLiftRope.attachment != liftTargetAnchor)
                    leftLiftRope.attachment = liftTargetAnchor;
                leftLiftRope.currentRopeLength = 0f;
            }

            if (rightLiftRope != null)
            {
                if (liftTargetAnchor != null && rightLiftRope.attachment != liftTargetAnchor)
                    rightLiftRope.attachment = liftTargetAnchor;
                rightLiftRope.currentRopeLength = 0f;
            }
        }

        private void OnDestroy()
        {
            if (deckRopeL != null && origRopeLTarget != null)
                deckRopeL.attachment = origRopeLTarget;

            if (deckRopeR != null && origRopeRTarget != null)
                deckRopeR.attachment = origRopeRTarget;

            if (yard != null) Destroy(yard.gameObject);
            if (leftFootRope != null) Destroy(leftFootRope.gameObject);
            if (rightFootRope != null) Destroy(rightFootRope.gameObject);
            if (leftLiftRope != null) Destroy(leftLiftRope.gameObject);
            if (rightLiftRope != null) Destroy(rightLiftRope.gameObject);
        }
    }

    internal static class YardMesh
    {
        private static Mesh mesh;

        public static Mesh Get()
        {
            if (mesh != null) return mesh;

            const int seg = 12, rings = 24;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();

            for (int i = 0; i <= rings; i++)
            {
                float x = i / (float)rings - 0.5f;
                float edge = Mathf.Min(i, rings - i) / (float)rings;
                float r = 1f + 0.35f * (1f - Mathf.SmoothStep(0f, 1f, edge / 0.06f));
                for (int j = 0; j <= seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    v.Add(new Vector3(x, r * Mathf.Cos(a), r * Mathf.Sin(a)));
                    uv.Add(new Vector2(i / (float)rings, j / (float)seg));
                }
            }
            int stride = seg + 1;
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a0 = i * stride + j, b0 = a0 + 1, c0 = a0 + stride, d0 = c0 + 1;
                    t.Add(a0); t.Add(b0); t.Add(c0);
                    t.Add(b0); t.Add(d0); t.Add(c0);
                }

            for (int end = 0; end < 2; end++)
            {
                float x = end == 0 ? -0.5f : 0.5f;
                float r = 1.35f;
                int center = v.Count;
                v.Add(new Vector3(x, 0f, 0f)); uv.Add(new Vector2(end, 0.5f));
                int ring0 = v.Count;
                for (int j = 0; j <= seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    v.Add(new Vector3(x, r * Mathf.Cos(a), r * Mathf.Sin(a)));
                    uv.Add(new Vector2(end, j / (float)seg));
                }
                for (int j = 0; j < seg; j++)
                {
                    if (end == 1) { t.Add(center); t.Add(ring0 + j); t.Add(ring0 + j + 1); }
                    else          { t.Add(center); t.Add(ring0 + j + 1); t.Add(ring0 + j); }
                }
            }

            mesh = new Mesh { name = "topsail_yard_spar" };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}