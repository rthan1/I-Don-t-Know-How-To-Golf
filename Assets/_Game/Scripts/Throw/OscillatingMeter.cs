using UnityEngine;

namespace Golf.Throwing
{
    /// <summary>
    /// Ping-pong meter used by the power and accuracy mini-games.
    /// Value is always in [0, 1].
    /// </summary>
    public sealed class OscillatingMeter
    {
        float _elapsed;
        float _speed;
        bool _running;

        public float Value { get; private set; }
        public bool IsRunning => _running;

        public void Start(float speed, float startValue = 0f)
        {
            _speed = Mathf.Max(0.01f, speed);
            _elapsed = Mathf.Clamp01(startValue) / _speed;
            Value = Mathf.PingPong(_elapsed * _speed, 1f);
            _running = true;
        }

        public void Tick(float deltaTime)
        {
            if (!_running)
                return;

            _elapsed += deltaTime;
            Value = Mathf.PingPong(_elapsed * _speed, 1f);
        }

        public float Stop()
        {
            _running = false;
            return Value;
        }

        public void Reset()
        {
            _running = false;
            _elapsed = 0f;
            Value = 0f;
        }
    }
}
