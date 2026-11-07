using UnityEngine;

namespace Golf.Throwing
{
    /// <summary>
    /// Ballistic aim arc preview of where the throw will travel.
    /// </summary>
    public sealed class ThrowAimVisualizer : MonoBehaviour
    {
        [SerializeField] int pointCount = 48;
        [SerializeField] float maxSimTime = 6f;
        [SerializeField] float timeStep = 0.05f;
        [SerializeField] float groundClearance = 0.02f;
        [SerializeField] float lineWidth = 0.04f;
        [SerializeField] Color lineColor = new Color(1f, 0.85f, 0.15f, 1f);

        readonly Vector3[] _points = new Vector3[128];

        LineRenderer _line;
        Material _material;

        public void Show(Vector3 origin, Vector3 flatDirection, float speed, float loftDegrees)
        {
            EnsureBuilt();
            _line.enabled = true;
            UpdateArc(origin, flatDirection, speed, loftDegrees);
        }

        public void UpdateArc(Vector3 origin, Vector3 flatDirection, float speed, float loftDegrees)
        {
            if (_line == null || !_line.enabled)
                return;

            Vector3 dir = flatDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector3.forward;
            dir.Normalize();

            Vector3 start = origin + Vector3.up * 0.12f;
            Vector3 initialVelocity = Quaternion.LookRotation(dir) * Quaternion.Euler(-loftDegrees, 0f, 0f) * Vector3.forward * Mathf.Max(0.1f, speed);
            Vector3 gravity = Physics.gravity;

            float groundY = EstimateGroundY(start);
            int written = 0;
            _points[written++] = start;

            float t = 0f;
            while (t < maxSimTime && written < _points.Length && written < pointCount)
            {
                t += timeStep;
                Vector3 p = start + initialVelocity * t + 0.5f * gravity * (t * t);

                if (t > 0.1f && p.y <= groundY + groundClearance)
                {
                    p.y = groundY + groundClearance;
                    _points[written++] = p;
                    break;
                }

                _points[written++] = p;
            }

            _line.positionCount = written;
            for (int i = 0; i < written; i++)
                _line.SetPosition(i, _points[i]);
        }

        public void Hide()
        {
            if (_line != null)
                _line.enabled = false;
        }

        void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }

        static float EstimateGroundY(Vector3 start)
        {
            if (Physics.Raycast(start + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 20f))
                return hit.point.y;

            return Mathf.Max(0f, start.y - 1.2f);
        }

        void EnsureBuilt()
        {
            if (_line != null)
                return;

            var go = new GameObject("ThrowAimArc");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.numCapVertices = 4;
            _line.numCornerVertices = 4;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.startWidth = lineWidth;
            _line.endWidth = lineWidth * 0.55f;
            _line.alignment = LineAlignment.View;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/Lit");
            _material = new Material(shader);
            if (_material.HasProperty("_BaseColor"))
                _material.SetColor("_BaseColor", lineColor);
            if (_material.HasProperty("_Color"))
                _material.SetColor("_Color", lineColor);
            _material.color = lineColor;
            _line.sharedMaterial = _material;
            _line.startColor = lineColor;
            _line.endColor = new Color(lineColor.r, lineColor.g, lineColor.b, 0.45f);
            _line.enabled = false;
        }
    }
}
