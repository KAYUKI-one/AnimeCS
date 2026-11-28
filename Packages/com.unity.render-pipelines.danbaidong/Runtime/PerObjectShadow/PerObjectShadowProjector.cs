using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// PerObjectShadow Projector component.
    /// </summary>
    [ExecuteAlways]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    [AddComponentMenu("Rendering/PerObjectShadow Projector")]
    public class PerObjectShadowProjector : MonoBehaviour
    {
        internal delegate void PerObjectShadowProjectorAction(PerObjectShadowProjector shadowProjector);
        internal static event PerObjectShadowProjectorAction onPerObjectShadowAdd;
        internal static event PerObjectShadowProjectorAction onPerObjectShadowRemove;
        internal static event PerObjectShadowProjectorAction onPerObjectShadowPropertyChange;
        internal static event Action onAllPerObjectShadowPropertyChange;
        internal static event PerObjectShadowProjectorAction onPerObjectShadowMaterialChange;
        internal static Material defaultMaterial { get; set; }
        internal static bool isSupported => onPerObjectShadowAdd != null;

        internal static RenderingLayerMask excludeLayer = 0;

        internal ObjectShadowEntity objectShadowEntity { get; set; }

        public static bool showDebugGzimos = true;

        [SerializeField]
        private Renderer[] m_Renderers;
        [SerializeField]
        private Material m_Material = null;
        [SerializeField]
        private float m_DrawDistance = 1000.0f;
        [SerializeField][Range(1.0f, 20.0f)]
        private float m_FarPlaneScale = 5.0f;

        private Material m_OldMaterial = null;

        /// <summary>
        /// Only collect renderers once.
        /// </summary>
        [SerializeField]
        private bool m_IsCollected = false;

        /// <summary>
        /// All renderers for rendering shadows.
        /// </summary>
        public Renderer[] childRenderers
        {
            get
            {
                return m_Renderers;
            }
        }

        /// <summary>
        /// The material used by the PerObjectShadow.
        /// </summary>
        public Material material
        {
            get
            {
                return m_Material;
            }
            set
            {
                m_Material = value;
                OnValidate();
            }
        }

        /// <summary>
        /// Distance from camera at which the PerObjectShadow is not rendered anymore.
        /// </summary>
        public float drawDistance
        {
            get
            {
                return m_DrawDistance;
            }
            set
            {
                m_DrawDistance = Mathf.Max(0f, value);
                OnValidate();
            }
        }

        /// <summary>
        /// Object Shadow map camera farPlane
        /// </summary>
        public float farPlaneScale
        {
            get
            {
                return m_FarPlaneScale;
            }
            set
            {
                m_FarPlaneScale = Mathf.Max(1.0f, value);
                OnValidate();
            }
        }

        private static Material s_RuntimeFallbackMaterial;

        void InitMaterial()
        {
            if (m_Material == null)
            {
#if UNITY_EDITOR
                m_Material = defaultMaterial;
#endif
                if (m_Material == null)
                {
                    if (s_RuntimeFallbackMaterial == null)
                    {
                        var shader = Shader.Find("PerObjectShadow/ShadowProjector");
                        if (shader != null)
                        {
                            s_RuntimeFallbackMaterial = new Material(shader)
                            {
                                name = "PerObjectShadowRuntimeDefault",
                                hideFlags = HideFlags.HideAndDontSave
                            };
                        }
                    }
                    m_Material = s_RuntimeFallbackMaterial;
                }
            }
        }

        /// <summary>
        /// ShadowProjector contains renderers in children.
        /// </summary>
        public void CollectRenderers()
        {
            var all = this.gameObject.GetComponentsInChildren<Renderer>();
            var casters = new System.Collections.Generic.List<Renderer>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].shadowCastingMode != ShadowCastingMode.Off)
                    casters.Add(all[i]);
            }
            m_Renderers = casters.ToArray();
            ExcludeMeshRenderersRenderingLayers();
            m_IsCollected = true;
        }

        private void ExcludeMeshRenderersRenderingLayers()
        {
            uint mask = excludeLayer.value;
            if (mask == 0 && UniversalRenderPipeline.asset != null)
                mask = UniversalRenderPipeline.asset.perObjectShadowExcludeLayer.value;
            if (mask != 0 && m_Renderers != null && m_Renderers.Length > 0)
            {
                for (int i = 0; i < m_Renderers.Length; i++)
                {
                    var renderer = m_Renderers[i];
                    if (renderer != null)
                        renderer.renderingLayerMask = renderer.renderingLayerMask & ~mask;
                }
            }
        }

        void OnEnable()
        {
            InitMaterial();

            m_OldMaterial = m_Material;

            if (!m_IsCollected)
                CollectRenderers();


            onPerObjectShadowAdd?.Invoke(this);

#if UNITY_EDITOR
            // Handle scene visibility
            //UnityEditor.SceneVisibilityManager.visibilityChanged += UpdatePerObjectShadowVisibility;
#endif
        }

#if UNITY_EDITOR
        //void UpdatePerObjectShadowVisibility()
        //{
        //    // Fade out the PerObjectShadow when it is hidden by the scene visibility
        //    if (UnityEditor.SceneVisibilityManager.instance.IsHidden(gameObject))
        //    {
        //        onPerObjectShadowRemove?.Invoke(this);
        //    }
        //    else
        //    {
        //        onPerObjectShadowAdd?.Invoke(this);
        //        onPerObjectShadowPropertyChange?.Invoke(this); // Scene culling mask may have changed.
        //    }
        //}

#endif

        void OnDisable()
        {
            onPerObjectShadowRemove?.Invoke(this);

#if UNITY_EDITOR
            //UnityEditor.SceneVisibilityManager.visibilityChanged -= UpdatePerObjectShadowVisibility;
#endif
        }

        internal void OnValidate()
        {
            if (!isActiveAndEnabled)
                return;

            if (m_Material != m_OldMaterial)
            {
                onPerObjectShadowMaterialChange?.Invoke(this);
                m_OldMaterial = m_Material;
            }
            else
                onPerObjectShadowPropertyChange?.Invoke(this);
        }


        /// <summary>
        /// Checks if material is valid for rendering PerObjectShadows.
        /// </summary>
        /// <returns>True if material is valid.</returns>
        public bool IsValid()
        {
            if (material == null)
                return false;

            if (m_Renderers.Length == 0)
                return false;

            if (material.FindPass(PerObjectShadowShaderPassNames.PerObjectShadowProjector) != -1)
                return true;

            return false;
        }

        internal static void UpdateAllPerObjectShadowProperties()
        {
            onAllPerObjectShadowPropertyChange?.Invoke();
        }

    }
}