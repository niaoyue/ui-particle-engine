using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UIParticleEngine.Editor
{
    /// <summary>
    /// Converts Unity ParticleSystem (Shuriken) setups into UIParticleEmitter equivalents.
    /// Every ParticleSystem in the source hierarchy becomes a RectTransform +
    /// UIParticleEmitter child with its modules mapped over, world units scaled to canvas
    /// pixels, and the renderer's main texture re-imported as the emitter sprite.
    /// Anything Shuriken-only (noise, collision, sub-emitters...) is listed in the report.
    /// </summary>
    public static class UIParticleMigrator
    {
        /// <summary>
        /// Migrates a prefab or scene object. Returns the new root GameObject
        /// (left in the scene; callers can save it as a prefab).
        /// </summary>
        public static GameObject MigrateHierarchy(GameObject source, float pixelsPerUnit, List<string> report)
        {
            var root = new GameObject(source.name + " (UI)", typeof(RectTransform));
            ParticleSystem[] systems = source.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0)
                report?.Add("No ParticleSystem found in the source hierarchy.");

            foreach (ParticleSystem ps in systems)
            {
                var child = new GameObject(ps.gameObject.name, typeof(RectTransform), typeof(CanvasRenderer));
                var rect = (RectTransform)child.transform;
                rect.SetParent(root.transform, false);

                // Flatten the transform chain: world offset from the source root, in pixels.
                Vector3 offset = source.transform.InverseTransformPoint(ps.transform.position);
                rect.anchoredPosition = new Vector2(offset.x, offset.y) * pixelsPerUnit;

                var emitter = child.AddComponent<UIParticleEmitter>();
                emitter.raycastTarget = false;
                MigrateSystem(ps, emitter, pixelsPerUnit, report);
            }
            return root;
        }

        /// <summary>Maps a single ParticleSystem's modules onto an existing emitter.</summary>
        public static void MigrateSystem(ParticleSystem ps, UIParticleEmitter emitter, float pixelsPerUnit,
            List<string> report)
        {
            string id = ps.gameObject.name;

            // --- Main ---
            ParticleSystem.MainModule psMain = ps.main;
            var main = emitter.Main;
            main.Duration = psMain.duration;
            main.Looping = psMain.loop;
            main.Prewarm = psMain.prewarm;
            main.StartDelay = ToRange(psMain.startDelay, 1f, id, "startDelay", report).Min;
            main.StartLifetime = ToRange(psMain.startLifetime, 1f, id, "startLifetime", report);
            main.StartSpeed = ToRange(psMain.startSpeed, pixelsPerUnit, id, "startSpeed", report);
            main.StartSize = ToRange(psMain.startSize, pixelsPerUnit, id, "startSize", report);
            main.StartRotation = ToRange(psMain.startRotation, Mathf.Rad2Deg, id, "startRotation", report);
            (main.StartColorA, main.StartColorB) = ToColorPair(psMain.startColor, id, "startColor", report);
            // Shuriken gravity uses Physics gravity in world units; ours is ×100 px per unit.
            main.GravityModifier = ToRange(psMain.gravityModifier, 1f, id, "gravityModifier", report).Min
                                   * (pixelsPerUnit / 100f);
            main.SimulationSpace = psMain.simulationSpace == ParticleSystemSimulationSpace.World
                ? UIParticleEmitter.SimulationSpace.World
                : UIParticleEmitter.SimulationSpace.Local;
            if (psMain.simulationSpace == ParticleSystemSimulationSpace.Custom)
                report?.Add($"[{id}] Custom simulation space mapped to Local.");
            main.SimulationSpeed = psMain.simulationSpeed;
            main.UseUnscaledTime = psMain.useUnscaledTime;
            main.PlayOnAwake = psMain.playOnAwake;
            main.MaxParticles = psMain.maxParticles;
            main.AutoRandomSeed = ps.useAutoRandomSeed;
            if (!ps.useAutoRandomSeed)
                main.RandomSeed = ps.randomSeed;

            // --- Emission ---
            ParticleSystem.EmissionModule psEmission = ps.emission;
            var emission = emitter.Emission;
            emission.Enabled = psEmission.enabled;
            emission.RateOverTime = ToRange(psEmission.rateOverTime, 1f, id, "rateOverTime", report).Min;
            emission.RateOverDistance =
                ToRange(psEmission.rateOverDistance, 1f, id, "rateOverDistance", report).Min / pixelsPerUnit;
            var bursts = new ParticleSystem.Burst[psEmission.burstCount];
            psEmission.GetBursts(bursts);
            var mapped = new UIParticleEmitter.Burst[bursts.Length];
            for (int i = 0; i < bursts.Length; i++)
            {
                mapped[i] = new UIParticleEmitter.Burst(
                    bursts[i].time,
                    ToRange(bursts[i].count, 1f, id, $"burst[{i}].count", report),
                    Mathf.Max(1, bursts[i].cycleCount),
                    Mathf.Max(0.01f, bursts[i].repeatInterval),
                    bursts[i].probability);
                if (bursts[i].cycleCount == 0)
                    report?.Add($"[{id}] burst[{i}] infinite cycles not supported; clamped to 1.");
            }
            emission.Bursts = mapped;

            // --- Shape ---
            ParticleSystem.ShapeModule psShape = ps.shape;
            var shape = emitter.Shape;
            shape.Enabled = psShape.enabled;
            shape.Radius = psShape.radius * pixelsPerUnit;
            shape.RadiusThickness = psShape.radiusThickness;
            shape.Arc = psShape.arc;
            shape.Rotation = psShape.rotation.z;
            switch (psShape.shapeType)
            {
                case ParticleSystemShapeType.Cone:
                case ParticleSystemShapeType.ConeVolume:
                    shape.ShapeType = UIParticleEmitter.ShapeType.Cone;
                    shape.Angle = psShape.angle;
                    break;
                case ParticleSystemShapeType.Circle:
                case ParticleSystemShapeType.Sphere:
                case ParticleSystemShapeType.Hemisphere:
                case ParticleSystemShapeType.Donut:
                    shape.ShapeType = UIParticleEmitter.ShapeType.Circle;
                    if (psShape.shapeType != ParticleSystemShapeType.Circle)
                        report?.Add($"[{id}] {psShape.shapeType} shape flattened to Circle.");
                    break;
                case ParticleSystemShapeType.Box:
                case ParticleSystemShapeType.BoxShell:
                case ParticleSystemShapeType.BoxEdge:
                case ParticleSystemShapeType.Rectangle:
                case ParticleSystemShapeType.SingleSidedEdge:
                    shape.ShapeType = UIParticleEmitter.ShapeType.Rect;
                    var boxRect = (RectTransform)emitter.transform;
                    Vector3 scale = psShape.scale;
                    boxRect.sizeDelta = new Vector2(
                        Mathf.Max(1f, Mathf.Abs(scale.x) * pixelsPerUnit),
                        Mathf.Max(1f, Mathf.Abs(scale.y) * pixelsPerUnit));
                    if (psShape.shapeType != ParticleSystemShapeType.Rectangle)
                        report?.Add($"[{id}] {psShape.shapeType} shape flattened to Rect (emitter rect sized from shape scale).");
                    break;
                default:
                    shape.ShapeType = UIParticleEmitter.ShapeType.Point;
                    report?.Add($"[{id}] {psShape.shapeType} shape not supported; using Point (radial).");
                    break;
            }

            // --- Velocity over lifetime ---
            ParticleSystem.VelocityOverLifetimeModule psVel = ps.velocityOverLifetime;
            if (psVel.enabled)
            {
                emitter.VelocityOverLifetime.Enabled = true;
                emitter.VelocityOverLifetime.Linear = new Vector2(
                    ToRange(psVel.x, pixelsPerUnit, id, "velocity.x", report).Min,
                    ToRange(psVel.y, pixelsPerUnit, id, "velocity.y", report).Min);
                if (psVel.orbitalZ.constant != 0f || psVel.orbitalZ.curveMultiplier != 0f)
                    report?.Add($"[{id}] orbital velocity not supported; only linear x/y migrated.");
            }

            // --- Limit velocity over lifetime ---
            ParticleSystem.LimitVelocityOverLifetimeModule psLimit = ps.limitVelocityOverLifetime;
            if (psLimit.enabled)
            {
                emitter.LimitVelocityOverLifetime.Enabled = true;
                emitter.LimitVelocityOverLifetime.Drag = ToRange(psLimit.drag, 1f, id, "drag", report).Min;
                if (psLimit.dampen > 0f)
                    report?.Add($"[{id}] limitVelocity dampen/speed clamp not supported; only drag migrated.");
            }

            // --- Color over lifetime ---
            ParticleSystem.ColorOverLifetimeModule psColor = ps.colorOverLifetime;
            if (psColor.enabled)
            {
                emitter.ColorOverLifetime.Enabled = true;
                if (psColor.color.mode == ParticleSystemGradientMode.Gradient && psColor.color.gradient != null)
                    emitter.ColorOverLifetime.Color = psColor.color.gradient;
                else
                    report?.Add($"[{id}] colorOverLifetime mode {psColor.color.mode} kept as default fade gradient.");
            }

            // --- Size over lifetime ---
            ParticleSystem.SizeOverLifetimeModule psSize = ps.sizeOverLifetime;
            if (psSize.enabled)
            {
                emitter.SizeOverLifetime.Enabled = true;
                emitter.SizeOverLifetime.Size = ToCurve(psSize.size, id, "sizeOverLifetime", report);
            }

            // --- Rotation over lifetime ---
            ParticleSystem.RotationOverLifetimeModule psRot = ps.rotationOverLifetime;
            if (psRot.enabled)
            {
                emitter.RotationOverLifetime.Enabled = true;
                emitter.RotationOverLifetime.AngularVelocity =
                    ToRange(psRot.z, Mathf.Rad2Deg, id, "rotationOverLifetime", report);
            }

            // --- Texture sheet animation ---
            ParticleSystem.TextureSheetAnimationModule psSheet = ps.textureSheetAnimation;
            if (psSheet.enabled)
            {
                var sheet = emitter.TextureSheetAnimation;
                sheet.Enabled = true;
                sheet.NumTilesX = psSheet.numTilesX;
                sheet.NumTilesY = psSheet.numTilesY;
                sheet.CycleCount = Mathf.Max(1, psSheet.cycleCount);
                sheet.RandomStartFrame = psSheet.startFrame.mode != ParticleSystemCurveMode.Constant
                                         || psSheet.startFrame.constant > 0f;
                if (psSheet.frameOverTime.mode == ParticleSystemCurveMode.Curve && psSheet.frameOverTime.curve != null)
                    sheet.FrameOverTime = psSheet.frameOverTime.curve;
                if (psSheet.animation == ParticleSystemAnimationType.SingleRow)
                    report?.Add($"[{id}] texture sheet SingleRow mode approximated as whole grid.");
            }

            // --- Trails ---
            ParticleSystem.TrailModule psTrails = ps.trails;
            if (psTrails.enabled)
            {
                var trails = emitter.Trails;
                trails.Enabled = true;
                trails.Ratio = psTrails.ratio;
                trails.Lifetime = ToRange(psTrails.lifetime, 1f, id, "trail.lifetime", report);
                trails.MinVertexDistance = Mathf.Max(0.01f, psTrails.minVertexDistance * pixelsPerUnit);
                trails.SizeAffectsWidth = psTrails.sizeAffectsWidth;
                trails.InheritParticleColor = psTrails.inheritParticleColor;
                trails.WidthOverTrail = ToCurve(psTrails.widthOverTrail, id, "widthOverTrail", report);
                if (psTrails.colorOverTrail.mode == ParticleSystemGradientMode.Gradient
                    && psTrails.colorOverTrail.gradient != null)
                    trails.ColorOverTrail = psTrails.colorOverTrail.gradient;
                if (!psTrails.dieWithParticles)
                    report?.Add($"[{id}] trails always die with their particle here (dieWithParticles=false ignored).");
            }

            // --- Renderer: material texture -> sprite ---
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                if (renderer.renderMode != ParticleSystemRenderMode.Billboard)
                    report?.Add($"[{id}] render mode {renderer.renderMode} not supported; drawing as billboards.");
                Material mat = renderer.sharedMaterial;
                if (mat != null && mat.mainTexture is Texture2D tex)
                {
                    Sprite sprite = FindOrCreateSprite(tex, report);
                    if (sprite != null)
                        emitter.Sprite = sprite;
                    string shader = mat.shader != null ? mat.shader.name : "";
                    if (shader.IndexOf("Additive", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        report?.Add($"[{id}] source material is additive — assign an additive UI material to the emitter for the same look.");
                }
            }

            // --- Modules with no equivalent ---
            if (ps.noise.enabled) report?.Add($"[{id}] noise module not supported; skipped.");
            if (ps.collision.enabled) report?.Add($"[{id}] collision module not supported; skipped.");
            if (ps.subEmitters.enabled) report?.Add($"[{id}] sub-emitters not supported; skipped.");
            if (ps.forceOverLifetime.enabled) report?.Add($"[{id}] forceOverLifetime not supported; skipped.");
            if (ps.inheritVelocity.enabled) report?.Add($"[{id}] inheritVelocity not supported; skipped.");
            if (ps.externalForces.enabled) report?.Add($"[{id}] externalForces not supported; skipped.");
            if (ps.sizeBySpeed.enabled) report?.Add($"[{id}] sizeBySpeed not supported; skipped.");
            if (ps.colorBySpeed.enabled) report?.Add($"[{id}] colorBySpeed not supported; skipped.");
            if (ps.rotationBySpeed.enabled) report?.Add($"[{id}] rotationBySpeed not supported; skipped.");
            if (ps.lights.enabled) report?.Add($"[{id}] lights module not supported; skipped.");
        }

        /// <summary>
        /// MinMaxCurve -> RangeF. Constants map exactly; curve modes are approximated by
        /// sampling the curve's extremes (noted in the report).
        /// </summary>
        internal static RangeF ToRange(ParticleSystem.MinMaxCurve curve, float scale, string id, string field,
            List<string> report)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return new RangeF(curve.constant * scale, curve.constant * scale);
                case ParticleSystemCurveMode.TwoConstants:
                    return new RangeF(curve.constantMin * scale, curve.constantMax * scale);
                default:
                {
                    float min = float.MaxValue, max = float.MinValue;
                    for (int i = 0; i <= 8; i++)
                    {
                        float t = i / 8f;
                        float lo = curve.curveMin != null
                            ? curve.curveMin.Evaluate(t)
                            : curve.curve.Evaluate(t);
                        float hi = curve.curveMax != null
                            ? curve.curveMax.Evaluate(t)
                            : curve.curve.Evaluate(t);
                        min = Mathf.Min(min, Mathf.Min(lo, hi));
                        max = Mathf.Max(max, Mathf.Max(lo, hi));
                    }
                    report?.Add($"[{id}] {field} uses a curve; approximated as random range over its extremes.");
                    return new RangeF(min * curve.curveMultiplier * scale, max * curve.curveMultiplier * scale);
                }
            }
        }

        /// <summary>MinMaxCurve (curve mode) -> AnimationCurve with the multiplier baked into the keys.</summary>
        internal static AnimationCurve ToCurve(ParticleSystem.MinMaxCurve curve, string id, string field,
            List<string> report)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return AnimationCurve.Constant(0f, 1f, curve.constant);
                case ParticleSystemCurveMode.TwoConstants:
                    report?.Add($"[{id}] {field} random-between-constants flattened to the max constant.");
                    return AnimationCurve.Constant(0f, 1f, curve.constantMax);
                default:
                {
                    AnimationCurve src = curve.curve ?? curve.curveMax;
                    if (src == null)
                        return AnimationCurve.Constant(0f, 1f, 1f);
                    var keys = src.keys;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i].value *= curve.curveMultiplier;
                        keys[i].inTangent *= curve.curveMultiplier;
                        keys[i].outTangent *= curve.curveMultiplier;
                    }
                    if (curve.mode == ParticleSystemCurveMode.TwoCurves)
                        report?.Add($"[{id}] {field} random-between-curves flattened to the upper curve.");
                    return new AnimationCurve(keys);
                }
            }
        }

        /// <summary>MinMaxGradient -> two-color random blend (gradients use their endpoints).</summary>
        internal static (Color, Color) ToColorPair(ParticleSystem.MinMaxGradient gradient, string id, string field,
            List<string> report)
        {
            switch (gradient.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return (gradient.color, gradient.color);
                case ParticleSystemGradientMode.TwoColors:
                    return (gradient.colorMin, gradient.colorMax);
                case ParticleSystemGradientMode.Gradient when gradient.gradient != null:
                    report?.Add($"[{id}] {field} gradient reduced to its endpoint colors.");
                    return (gradient.gradient.Evaluate(0f), gradient.gradient.Evaluate(1f));
                case ParticleSystemGradientMode.TwoGradients when gradient.gradientMin != null:
                    report?.Add($"[{id}] {field} two-gradients reduced to each gradient's midpoint.");
                    return (gradient.gradientMin.Evaluate(0.5f), gradient.gradientMax.Evaluate(0.5f));
                default:
                    return (Color.white, Color.white);
            }
        }

        /// <summary>
        /// Finds a Sprite sub-asset for the texture, converting the importer to Sprite
        /// import (single, no mips) first when needed.
        /// </summary>
        private static Sprite FindOrCreateSprite(Texture2D texture, List<string> report)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
            {
                report?.Add($"Texture '{texture.name}' is not an asset; sprite not assigned.");
                return null;
            }

            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub is Sprite existing)
                    return existing;

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                report?.Add($"Texture '{texture.name}' re-imported as a Sprite.");
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }

            report?.Add($"Texture '{texture.name}' could not be imported as a Sprite.");
            return null;
        }
    }

    /// <summary>Editor window front-end for <see cref="UIParticleMigrator"/>.</summary>
    public class UIParticleMigratorWindow : EditorWindow
    {
        private GameObject _source;
        private float _pixelsPerUnit = 100f;
        private bool _saveAsPrefab = true;
        private string _outputFolder = "Assets";
        private Vector2 _scroll;
        private readonly List<string> _report = new List<string>();

        [MenuItem("Tools/UI Particle Engine/Particle Migrator")]
        private static void Open() => GetWindow<UIParticleMigratorWindow>("Particle Migrator");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Converts every ParticleSystem in a prefab (or scene object) into UIParticleEmitters. " +
                "World units are scaled by Pixels Per Unit; the renderer's main texture becomes the emitter sprite.",
                MessageType.Info);

            _source = (GameObject)EditorGUILayout.ObjectField("Source", _source, typeof(GameObject), true);
            _pixelsPerUnit = EditorGUILayout.FloatField(
                new GUIContent("Pixels Per Unit", "Canvas pixels per Shuriken world unit (speed, size, radius, gravity)."),
                _pixelsPerUnit);
            _saveAsPrefab = EditorGUILayout.Toggle("Save As Prefab", _saveAsPrefab);
            if (_saveAsPrefab)
                _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);

            using (new EditorGUI.DisabledScope(_source == null))
            {
                if (GUILayout.Button("Migrate"))
                    Run();
            }

            if (_report.Count > 0)
            {
                EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                foreach (string line in _report)
                    EditorGUILayout.LabelField("• " + line, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        private void Run()
        {
            _report.Clear();
            GameObject result = UIParticleMigrator.MigrateHierarchy(_source, _pixelsPerUnit, _report);

            if (_saveAsPrefab)
            {
                string path = AssetDatabase.GenerateUniqueAssetPath(
                    $"{_outputFolder.TrimEnd('/')}/{result.name}.prefab");
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(result, path);
                Object.DestroyImmediate(result);
                _report.Add($"Saved prefab: {path}");
                EditorGUIUtility.PingObject(prefab);
            }
            else
            {
                _report.Add($"Created '{result.name}' in the open scene (drop it under a Canvas).");
                Selection.activeGameObject = result;
            }
        }
    }
}
