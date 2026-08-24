using UnityEditor;
using UnityEngine;

namespace UIParticleEngine.Editor
{
    /// <summary>
    /// ParticleSystem-style inspector: the Main module flat on top, then one
    /// toggleable boxed section per module, and a Renderer section at the bottom.
    /// </summary>
    [CustomEditor(typeof(UIParticleEmitter))]
    [CanEditMultipleObjects]
    public class UIParticleEmitterEditor : UnityEditor.Editor
    {
        private SerializedProperty _main;
        private SerializedProperty _emission;
        private SerializedProperty _shape;
        private SerializedProperty _velocityOverLifetime;
        private SerializedProperty _limitVelocityOverLifetime;
        private SerializedProperty _colorOverLifetime;
        private SerializedProperty _sizeOverLifetime;
        private SerializedProperty _rotationOverLifetime;
        private SerializedProperty _textureSheetAnimation;
        private SerializedProperty _trails;
        private SerializedProperty _sprite;

        private double _lastEditorTime;
        private bool _previewing;
        private SerializedProperty _color;
        private SerializedProperty _material;
        private SerializedProperty _raycastTarget;
        private SerializedProperty _raycastPadding;
        private SerializedProperty _maskable;

        private void OnEnable()
        {
            _main = serializedObject.FindProperty("_Main");
            _emission = serializedObject.FindProperty("_Emission");
            _shape = serializedObject.FindProperty("_Shape");
            _velocityOverLifetime = serializedObject.FindProperty("_VelocityOverLifetime");
            _limitVelocityOverLifetime = serializedObject.FindProperty("_LimitVelocityOverLifetime");
            _colorOverLifetime = serializedObject.FindProperty("_ColorOverLifetime");
            _sizeOverLifetime = serializedObject.FindProperty("_SizeOverLifetime");
            _rotationOverLifetime = serializedObject.FindProperty("_RotationOverLifetime");
            _textureSheetAnimation = serializedObject.FindProperty("_TextureSheetAnimation");
            _trails = serializedObject.FindProperty("_Trails");
            _sprite = serializedObject.FindProperty("_Sprite");
            _color = serializedObject.FindProperty("m_Color");
            _material = serializedObject.FindProperty("m_Material");
            _raycastTarget = serializedObject.FindProperty("m_RaycastTarget");
            _raycastPadding = serializedObject.FindProperty("m_RaycastPadding");
            _maskable = serializedObject.FindProperty("m_Maskable");
        }

        private void OnDisable()
        {
            StopPreview();
        }

        // ---------- edit-mode preview ----------

        private void OnEditorUpdate()
        {
            if (!_previewing || Application.isPlaying)
                return;

            var emitter = target as UIParticleEmitter;
            if (emitter == null)
            {
                StopPreview();
                return;
            }

            // EditorApplication.update can fire at several hundred Hz; ticking (and
            // repainting the whole editor) faster than ~60 fps is pure waste.
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastEditorTime < 1.0 / 60.0)
                return;
            float dt = Mathf.Clamp((float)(now - _lastEditorTime), 0f, 0.1f);
            _lastEditorTime = now;

            emitter.Simulate(dt);

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            Repaint();
        }

        private void StartPreview()
        {
            var emitter = target as UIParticleEmitter;
            if (emitter == null)
                return;
            _lastEditorTime = EditorApplication.timeSinceStartup;
            _previewing = true;
            EditorApplication.update += OnEditorUpdate; // subscribed only while previewing
            emitter.Play();
        }

        private void StopPreview()
        {
            if (!_previewing)
                return;
            _previewing = false;
            EditorApplication.update -= OnEditorUpdate;
            var emitter = target as UIParticleEmitter;
            if (emitter != null && !Application.isPlaying)
            {
                emitter.Stop(UIParticleEmitter.StopBehavior.StopEmittingAndClear);
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawChildren(_main);

            EditorGUILayout.Space(6f);
            DrawModule(_emission, "Emission");
            DrawModule(_shape, "Shape");
            DrawModule(_velocityOverLifetime, "Velocity over Lifetime");
            DrawModule(_limitVelocityOverLifetime, "Limit Velocity over Lifetime");
            DrawModule(_colorOverLifetime, "Color over Lifetime");
            DrawModule(_sizeOverLifetime, "Size over Lifetime");
            DrawModule(_rotationOverLifetime, "Rotation over Lifetime");
            DrawModule(_textureSheetAnimation, "Texture Sheet Animation");
            DrawModule(_trails, "Trails");

            DrawRendererSection();
            DrawPlaybackControls();

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawModule(SerializedProperty module, string label)
        {
            SerializedProperty enabled = module.FindPropertyRelative("Enabled");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Rect row = EditorGUILayout.GetControlRect();
            // BeginProperty gives the toggle prefab-override bolding, right-click
            // revert and mixed-value display under multi-select.
            GUIContent content = EditorGUI.BeginProperty(row, new GUIContent(label), enabled);
            EditorGUI.showMixedValue = enabled.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            bool value = EditorGUI.ToggleLeft(row, content, enabled.boolValue, EditorStyles.boldLabel);
            if (EditorGUI.EndChangeCheck())
                enabled.boolValue = value;
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();

            if (enabled.boolValue)
            {
                EditorGUI.indentLevel++;
                DrawChildren(module, skip: "enabled");
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawChildren(SerializedProperty parent, string skip = null)
        {
            SerializedProperty it = parent.Copy();
            SerializedProperty end = it.GetEndProperty();
            if (!it.NextVisible(true))
                return;

            while (!SerializedProperty.EqualContents(it, end))
            {
                if (skip == null || it.name != skip)
                    EditorGUILayout.PropertyField(it, true);
                if (!it.NextVisible(false))
                    break;
            }
        }

        private void ForEachTarget(System.Action<UIParticleEmitter> action)
        {
            foreach (Object t in targets)
            {
                if (t is UIParticleEmitter e)
                    action(e);
            }
        }

        private void DrawRendererSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Renderer", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_sprite);
            EditorGUILayout.PropertyField(_color, new GUIContent("Tint"));
            EditorGUILayout.PropertyField(_material);
            EditorGUILayout.PropertyField(_raycastTarget);
            if (_raycastTarget.boolValue)
                EditorGUILayout.PropertyField(_raycastPadding);
            EditorGUILayout.PropertyField(_maskable);
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private void DrawPlaybackControls()
        {
            EditorGUILayout.Space(4f);
            var emitter = (UIParticleEmitter)target;

            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField($"Particles: {emitter.ParticleCount}   Time: {emitter.Time:F2}", EditorStyles.miniLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(emitter.IsPaused ? "Resume" : "Play"))
                    ForEachTarget(e => e.Play());
                if (GUILayout.Button("Pause"))
                    ForEachTarget(e => e.Pause());
                if (GUILayout.Button("Stop"))
                    ForEachTarget(e => e.Stop());
                if (GUILayout.Button("Clear"))
                    ForEachTarget(e => e.Clear());
                EditorGUILayout.EndHorizontal();
                return;
            }

            if (targets.Length > 1)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                _previewing ? $"Preview   Particles: {emitter.ParticleCount}   Time: {emitter.Time:F2}" : "Preview",
                EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (!_previewing)
            {
                if (GUILayout.Button("Play"))
                    StartPreview();
            }
            else
            {
                if (GUILayout.Button("Restart"))
                {
                    emitter.Stop(UIParticleEmitter.StopBehavior.StopEmittingAndClear);
                    emitter.Play();
                }
                if (GUILayout.Button("Stop"))
                    StopPreview();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
    }
}
