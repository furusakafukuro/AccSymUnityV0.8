// StatusHUD.cs
using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public sealed class StatusHUD : MonoBehaviour
{
    [Serializable]
    public struct Status
    {
        public float fps;

        public int particleCount;
        public int dueThisFrame;
        public int activePlanned;
        public int integratedThisFrame;
        public int missedThisFrame;

        public double requestedSpeed;   // phys s / anim s
        public double effectiveSpeed;   // phys s / anim s

        public bool limited;
        public bool limitedByPhysics;
        public bool limitedByBudget;
        public bool limitedByBudgetStep0;
        public bool pausedNoField;

        public double budgetMs;
        public double usedMs;

        public int stepsPlanned;
        public int stepsPerformed;

        public double dtPhysStep;
        public double dtPhysFrameReq;
        public double dtPhysFrameMax;
    }

    public Text text;

    [Tooltip("UI更新間隔（秒）。毎フレーム更新しない＝GC/負荷を抑える。")]
    public float updateIntervalSec = 0.2f;

    private float lastUpdateTime;
    private readonly StringBuilder sb = new StringBuilder(1024);

    public void SetStatus(Status s)
    {
        if (text == null) return;

        float now = Time.unscaledTime;
        if (now - lastUpdateTime < updateIntervalSec) return;
        lastUpdateTime = now;

        double reqNsPerSec = s.requestedSpeed * 1e9;
        double effNsPerSec = s.effectiveSpeed * 1e9;

        sb.Clear();
        sb.Append("FPS: ").Append(s.fps.ToString("F1")).Append('\n');

        if (s.pausedNoField)
        {
            sb.Append("STATE: PAUSED (no fieldProvider)\n");
        }

        sb.Append("Due/Active/Int/Miss: ")
          .Append(s.dueThisFrame).Append('/')
          .Append(s.activePlanned).Append('/')
          .Append(s.integratedThisFrame).Append('/')
          .Append(s.missedThisFrame)
          .Append(" (total ").Append(s.particleCount).Append(')').Append('\n');

        sb.Append("Requested: ").Append(reqNsPerSec.ToString("F3")).Append(" ns/s").Append('\n');
        sb.Append("Effective: ").Append(effNsPerSec.ToString("F3")).Append(" ns/s");

        if (s.limited)
        {
            sb.Append(" [LIMITED");
            if (s.limitedByPhysics) sb.Append(":PHYS");
            if (s.limitedByBudget) sb.Append(":CPU");
            if (s.limitedByBudgetStep0) sb.Append(":STEP0");
            sb.Append(']');
        }
        sb.Append('\n');

        sb.Append("Budget: ").Append(s.usedMs.ToString("F2")).Append('/').Append(s.budgetMs.ToString("F2")).Append(" ms").Append('\n');
        sb.Append("Steps: ").Append(s.stepsPerformed).Append('/').Append(s.stepsPlanned).Append('\n');
        sb.Append("dtPhysStep: ").Append((s.dtPhysStep * 1e12).ToString("F2")).Append(" ps").Append('\n');
        sb.Append("dtPhysFrame req/max: ")
          .Append((s.dtPhysFrameReq * 1e12).ToString("F2")).Append(" / ")
          .Append((s.dtPhysFrameMax * 1e12).ToString("F2")).Append(" ps");

        text.text = sb.ToString();
    }
}