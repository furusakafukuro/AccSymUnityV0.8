// SimulationController.cs
using System;
using UnityEngine;

public sealed class SimulationController : MonoBehaviour
{
    [Header("Target (Quest 3)")]
    [Tooltip("Physics compute time budget per frame [ms]. Quest3/60fps: 2ms recommended.")]
    public float budgetMs = 2.0f;

    [Header("Time (Anim -> Phys)")]
    [Tooltip("Base physical seconds advanced per 1 animation second (s/s). Multiplied by fastFwd.")]
    public double basePhysPerAnimSecond = 1e-9; // 1 anim-sec => 1 ns phys
    [Tooltip("Fast-forward factor (>=1).")]
    public int fastFwd = 1;
    [Tooltip("Override dtAnim (seconds). If <=0, use Time.deltaTime.")]
    public float dtAnimOverride = 0.0f;

    [Header("Relativistic")]
    public float speedOfLight = 299792458f;

    [Header("Integrator Control")]
    [Tooltip("Max substeps per frame.")]
    public int maxStepsPerFrame = 32;
    [Tooltip("Boris stability margin: ωc*dt <= epsB")]
    public float epsB = 0.1f;
    [Tooltip("Spatial margin: v*dt <= epsX*dx")]
    public float epsX = 0.1f;
    [Tooltip("Characteristic length of field sampling / geometry scale [m].")]
    public float dx = 0.005f;

    [Header("Particles (SI units assumed)")]
    public int particleCount = 300;
    public float particleMass = 1.6726219e-27f;   // [kg]
    public float particleCharge = 1.6021766e-19f; // [C]
    public float initialSpeed = 3.0e7f;           // [m/s]
    public Vector3 spawnCenter = Vector3.zero;    // [m]
    public float spawnRadius = 0.02f;             // [m]

    [Header("LOD (Update Interval by distance)")]
    public Transform referenceCamera;
    public float nearDist = 1.0f; // [m]
    public float midDist = 3.0f;  // [m]
    public int nearInterval = 1;
    public int midInterval = 2;
    public int farInterval = 4;

    [Header("Load Shedding (minimal)")]
    [Tooltip("Max particles integrated this frame (round-robin).")]
    public int maxActivePerFrame = 120;

    [Header("Field Provider")]
    public MonoBehaviour fieldProviderBehaviour;

    [Header("Bmax Safety")]
    [Tooltip("If FieldProvider returns <=0, use this conservative fallback [Tesla].")]
    public float bMaxFallbackTesla = 1.0f;

    [Header("Optional Render Transforms")]
    public Transform[] particleVisuals;

    [Header("Render Extrapolation (visual only)")]
    [Tooltip("Clamp = interval * dtPhysFrameDone * factor.")]
    public float extrapolateFactor = 1.0f;
    [Tooltip("Absolute clamp for extrapolation [phys seconds]. 0 disables (not recommended).")]
    public double maxExtrapolatePhysSec = 0.0;

    [Header("Status UI")]
    public StatusHUD statusHUD;

    [Header("Debug Logs")]
    public bool enableDebugLogs = true;
    public int debugLogIntervalFrames = 120;

    // ---- runtime ----
    private IFieldProvider fieldProvider;

    private ParticleState[] stateCurr;
    private int[] updateInterval;

    // scheduling: due 판단은 lastScheduledFrame 기준 (active로 “確定”된 프레임만 갱신)
    private int[] lastScheduledFrame;
    private int[] lastIntegratedFrame;
    private double[] lastIntegratedTPhys;

    private bool[] dueThisFrame;
    private bool[] activeThisFrame;

    private int frameIndex;
    private int rrCursor;

    private double tPhys;
    private double lastDtPhysFrameDone;

    private bool limitedByPhysics;
    private bool limitedByBudget;
    private bool limitedByBudgetStep0;

    private void Awake()
    {
        fieldProvider = fieldProviderBehaviour as IFieldProvider;
        if (fieldProvider == null)
        {
            UnityEngine.Debug.LogError("[Sim] Field provider missing or not IFieldProvider. Physics will be paused.");
        }

        particleCount = Mathf.Max(1, particleCount);
        maxActivePerFrame = Mathf.Clamp(maxActivePerFrame, 1, particleCount);
        maxStepsPerFrame = Mathf.Max(1, maxStepsPerFrame);
        if (bMaxFallbackTesla <= 0f) bMaxFallbackTesla = 1.0f;

        stateCurr = new ParticleState[particleCount];
        updateInterval = new int[particleCount];

        lastScheduledFrame = new int[particleCount];
        lastIntegratedFrame = new int[particleCount];
        lastIntegratedTPhys = new double[particleCount];

        dueThisFrame = new bool[particleCount];
        activeThisFrame = new bool[particleCount];

        InitializeParticles();

        for (int i = 0; i < particleCount; i++)
        {
            updateInterval[i] = Mathf.Max(1, nearInterval);
            lastScheduledFrame[i] = 0;
            lastIntegratedFrame[i] = 0;
            lastIntegratedTPhys[i] = tPhys;
        }

        rrCursor = 0;
        lastDtPhysFrameDone = 0.0;

        CheckScalingWarnings();

        if (enableDebugLogs)
        {
            UnityEngine.Debug.Log($"[Sim] Awake: particles={particleCount}, budgetMs={budgetMs}, maxStepsPerFrame={maxStepsPerFrame}, maxActivePerFrame={maxActivePerFrame}");
        }
    }

    private void CheckScalingWarnings()
    {
        if (dx <= 0f) UnityEngine.Debug.LogError("[Sim] dx must be > 0 (meters).");
        if (spawnRadius > 10f || nearDist > 1000f || midDist > 5000f)
        {
            UnityEngine.Debug.LogWarning("[Sim] Distances look large. Ensure Unity unit = meter or add scaling (metersToUnity).");
        }
        if (initialSpeed > 2.0e8f)
        {
            UnityEngine.Debug.LogWarning("[Sim] initialSpeed is high. Relativistic integrator OK, but check your intended regime.");
        }
        if (referenceCamera == null)
        {
            UnityEngine.Debug.LogWarning("[Sim] referenceCamera is null. LOD distances measured from world origin.");
        }
        if (particleVisuals != null && particleVisuals.Length > 0 && particleVisuals.Length < particleCount)
        {
            UnityEngine.Debug.LogWarning($"[Sim] particleVisuals.Length({particleVisuals.Length}) < particleCount({particleCount}). Only first visuals updated.");
        }
    }

    private void InitializeParticles()
    {
        UnityEngine.Random.InitState(12345);

        for (int i = 0; i < particleCount; i++)
        {
            Vector3 p = spawnCenter + UnityEngine.Random.insideUnitSphere * spawnRadius;
            Vector3 dir = UnityEngine.Random.onUnitSphere;
            Vector3 v0 = dir * initialSpeed;

            Vector3 u0 = ParticleIntegratorRelativisticBorisU.UFromV(v0, speedOfLight);

            stateCurr[i] = new ParticleState
            {
                position = p,
                u = u0,
                charge = particleCharge,
                mass = particleMass
            };
        }
    }

    private void Update()
    {
        frameIndex++;

        float dtAnim = (dtAnimOverride > 0f) ? dtAnimOverride : Time.deltaTime;
        if (dtAnim <= 0f) return;

        // No field: freeze physics time, render current
        if (fieldProvider == null)
        {
            lastDtPhysFrameDone = 0.0;
            RenderParticles(tPhys);
            UpdateHUD(dtAnim, pausedNoField: true,
                      dueTotal: 0, activePlanned: 0, integrated: 0, missed: 0,
                      requestedSpeed: 0, effectiveSpeed: 0,
                      usedMs: 0, stepsPlanned: 0, stepsPerformed: 0,
                      dtPhysStep: 0, dtPhysFrameReq: 0, dtPhysFrameMax: 0,
                      limited: true, limitedPhys: false, limitedCpu: true, step0: true);
            EmitPeriodicWarning("[Sim] PAUSED: fieldProvider is null.");
            return;
        }

        // Requested physical time this frame
        double requestedSpeed = basePhysPerAnimSecond * Math.Max(1, fastFwd);
        double dtPhysFrameReq = dtAnim * requestedSpeed;

        // Physics dt constraints (global conservative)
        float vmax = EstimateMaxSpeed();
        float omegaCmax = EstimateOmegaCMaxSafe(); // conservative (gamma=1 worst-case)

        double dtMaxB = (omegaCmax > 0f) ? (epsB / omegaCmax) : double.PositiveInfinity;
        double dtMaxX = (vmax > 0f) ? (epsX * dx / vmax) : double.PositiveInfinity;

        double dtPhysStepMax = Math.Min(dtMaxB, dtMaxX);
        if (!double.IsFinite(dtPhysStepMax) || dtPhysStepMax <= 0.0)
        {
            dtPhysStepMax = dtPhysFrameReq;
            EmitPeriodicWarning($"[Sim] dtPhysStepMax fallback. dtPhysFrameReq={dtPhysFrameReq:E3}s");
        }

        double dtPhysFrameMax = dtPhysStepMax * Math.Max(1, maxStepsPerFrame);

        limitedByPhysics = dtPhysFrameReq > dtPhysFrameMax;
        limitedByBudget = false;
        limitedByBudgetStep0 = false;

        double dtPhysFramePlan = limitedByPhysics ? dtPhysFrameMax : dtPhysFrameReq;

        int stepsPlanned = (int)Math.Ceiling(dtPhysFramePlan / dtPhysStepMax);
        stepsPlanned = Mathf.Clamp(stepsPlanned, 1, maxStepsPerFrame);
        double dtPhysStep = dtPhysFramePlan / stepsPlanned;

        // LOD update
        UpdateLODIntervals();

        // due backlog
        int dueTotal = 0;
        for (int i = 0; i < particleCount; i++)
        {
            bool due = IsDue(i);
            dueThisFrame[i] = due;
            activeThisFrame[i] = false;
            if (due) dueTotal++;
        }

        // If nothing due: freeze tPhys (avoid RF phase drifting without motion)
        if (dueTotal == 0)
        {
            lastDtPhysFrameDone = 0.0;
            RenderParticles(tPhys);
            UpdateHUD(dtAnim, pausedNoField: false,
                      dueTotal: 0, activePlanned: 0, integrated: 0, missed: 0,
                      requestedSpeed: requestedSpeed, effectiveSpeed: 0,
                      usedMs: 0, stepsPlanned: stepsPlanned, stepsPerformed: 0,
                      dtPhysStep: dtPhysStep, dtPhysFrameReq: dtPhysFrameReq, dtPhysFrameMax: dtPhysFrameMax,
                      limited: limitedByPhysics, limitedPhys: limitedByPhysics, limitedCpu: false, step0: false);
            return;
        }

        // Choose active subset (round-robin)
        int processLimit = Mathf.Clamp(maxActivePerFrame, 1, particleCount);
        int activePlanned = 0;

        if (dueTotal <= processLimit)
        {
            for (int i = 0; i < particleCount; i++)
            {
                if (!dueThisFrame[i]) continue;
                activeThisFrame[i] = true;
                activePlanned++;
            }
        }
        else
        {
            int scanned = 0;
            int idx = rrCursor;
            int lastChosen = rrCursor;

            while (scanned < particleCount && activePlanned < processLimit)
            {
                if (dueThisFrame[idx])
                {
                    activeThisFrame[idx] = true;
                    activePlanned++;
                    lastChosen = idx;
                }
                idx++;
                if (idx >= particleCount) idx = 0;
                scanned++;
            }

            rrCursor = lastChosen + 1;
            if (rrCursor >= particleCount) rrCursor = 0;
        }

        // Physics steps within CPU budget
        double tPhysStart = tPhys;
        float tStart = Time.realtimeSinceStartup;

        int performedSteps = 0;
        for (int s = 0; s < stepsPlanned; s++)
        {
            if ((Time.realtimeSinceStartup - tStart) * 1000.0f > budgetMs)
            {
                limitedByBudget = true;
                if (performedSteps == 0) limitedByBudgetStep0 = true;
                break;
            }

            StepActiveParticles(dtPhysStep);
            tPhys += dtPhysStep;
            performedSteps++;
        }

        if (performedSteps < stepsPlanned) limitedByBudget = true;

        double dtPhysFrameDone = dtPhysStep * performedSteps;
        lastDtPhysFrameDone = dtPhysFrameDone;
        double tPhysEnd = tPhysStart + dtPhysFrameDone; // == tPhys

        // Commit schedule/integrated ONLY if at least one step happened
        int integratedThisFrame = 0;
        if (performedSteps > 0)
        {
            for (int i = 0; i < particleCount; i++)
            {
                if (!activeThisFrame[i]) continue;

                lastScheduledFrame[i] = frameIndex;     // active chosen and actually advanced
                lastIntegratedFrame[i] = frameIndex;
                lastIntegratedTPhys[i] = tPhysEnd;
                integratedThisFrame++;
            }
        }

        int missedThisFrame = Mathf.Max(0, dueTotal - integratedThisFrame);

        // Render (visual extrapolation)
        RenderParticles(tPhysEnd);

        // HUD
        double effectiveSpeed = (performedSteps > 0) ? (dtPhysFrameDone / dtAnim) : 0.0;
        bool limited = limitedByPhysics || limitedByBudget;

        UpdateHUD(dtAnim, pausedNoField: false,
                  dueTotal: dueTotal, activePlanned: activePlanned, integrated: integratedThisFrame, missed: missedThisFrame,
                  requestedSpeed: requestedSpeed, effectiveSpeed: effectiveSpeed,
                  usedMs: (Time.realtimeSinceStartup - tStart) * 1000.0,
                  stepsPlanned: stepsPlanned, stepsPerformed: performedSteps,
                  dtPhysStep: dtPhysStep, dtPhysFrameReq: dtPhysFrameReq, dtPhysFrameMax: dtPhysFrameMax,
                  limited: limited, limitedPhys: limitedByPhysics, limitedCpu: limitedByBudget, step0: limitedByBudgetStep0);

        // Logs (throttled)
        if (enableDebugLogs && ShouldLogThisFrame())
        {
            if (limited)
            {
                string reason = (limitedByPhysics ? "PHYS " : "") + (limitedByBudget ? "CPU " : "") + (limitedByBudgetStep0 ? "STEP0 " : "");
                UnityEngine.Debug.LogWarning(
                    $"[Sim] LIMITED({reason.Trim()}): due={dueTotal} active={activePlanned} integrated={integratedThisFrame} " +
                    $"steps={performedSteps}/{stepsPlanned} usedMs={(Time.realtimeSinceStartup - tStart) * 1000.0f:F2}/{budgetMs:F2} " +
                    $"dtPhysStep={dtPhysStep:E3}s dtPhysFrameDone={dtPhysFrameDone:E3}s");
            }
            else
            {
                UnityEngine.Debug.Log($"[Sim] OK: due={dueTotal} active={activePlanned} steps={performedSteps}/{stepsPlanned} dtPhysFrameDone={dtPhysFrameDone:E3}s");
            }
        }
    }

    private void StepActiveParticles(double dt)
    {
        float dtf = (float)dt;

        for (int i = 0; i < particleCount; i++)
        {
            if (!activeThisFrame[i]) continue;

            ParticleState s = stateCurr[i];

            Vector3 E = fieldProvider.GetE(s.position, tPhys);
            Vector3 B = fieldProvider.GetB(s.position, tPhys);

            ParticleIntegratorRelativisticBorisU.Step(ref s, E, B, dtf, speedOfLight);

            stateCurr[i] = s;
        }
    }

    private bool IsDue(int i)
    {
        int interval = updateInterval[i];
        if (interval <= 1) return true;
        return (frameIndex - lastScheduledFrame[i]) >= interval;
    }

    private void RenderParticles(double tPhysRender)
    {
        if (particleVisuals == null || particleVisuals.Length == 0) return;

        int n = Mathf.Min(particleVisuals.Length, particleCount);
        double factor = Math.Max(0.0, extrapolateFactor);

        for (int i = 0; i < n; i++)
        {
            Transform tr = particleVisuals[i];
            if (tr == null) continue;

            double dtSince = tPhysRender - lastIntegratedTPhys[i];
            if (dtSince < 0.0) dtSince = 0.0;

            double dtClamp = updateInterval[i] * lastDtPhysFrameDone * factor;
            if (maxExtrapolatePhysSec > 0.0) dtClamp = Math.Min(dtClamp, maxExtrapolatePhysSec);
            if (dtClamp < 0.0) dtClamp = 0.0;

            double dtUse = Math.Min(dtSince, dtClamp);

            Vector3 p = stateCurr[i].position;
            Vector3 v = ParticleIntegratorRelativisticBorisU.VelocityFromU(stateCurr[i].u, speedOfLight);

            tr.position = p + v * (float)dtUse;
        }
    }

    private void UpdateLODIntervals()
    {
        Vector3 camPos = (referenceCamera != null) ? referenceCamera.position : Vector3.zero;
        float near2 = nearDist * nearDist;
        float mid2 = midDist * midDist;

        int ni = Mathf.Max(1, nearInterval);
        int mi = Mathf.Max(1, midInterval);
        int fi = Mathf.Max(1, farInterval);

        for (int i = 0; i < particleCount; i++)
        {
            float d2 = (stateCurr[i].position - camPos).sqrMagnitude;
            if (d2 <= near2) updateInterval[i] = ni;
            else if (d2 <= mid2) updateInterval[i] = mi;
            else updateInterval[i] = fi;
        }
    }

    private float EstimateMaxSpeed()
    {
        float vmax = 0f;
        float c = speedOfLight;

        for (int i = 0; i < particleCount; i++)
        {
            float v = ParticleIntegratorRelativisticBorisU.SpeedFromU(stateCurr[i].u, c);
            if (v > vmax) vmax = v;
        }
        return vmax;
    }

    private float EstimateOmegaCMaxSafe()
    {
        // ωc = |q|B/(γm). Worst-case is γ=1 -> conservative dt bound.
        float bMax = fieldProvider.GetBMaxEstimate();
        if (bMax <= 0f)
        {
            bMax = bMaxFallbackTesla;
            EmitPeriodicWarning($"[Sim] BMaxEstimate<=0. Using fallback bMax={bMax:F3} T. Set BMaxEstimate for correct dt control.");
        }

        float qOverM = Mathf.Abs(particleCharge / particleMass);
        return qOverM * bMax;
    }

    private void UpdateHUD(
        float dtAnim,
        bool pausedNoField,
        int dueTotal,
        int activePlanned,
        int integrated,
        int missed,
        double requestedSpeed,
        double effectiveSpeed,
        double usedMs,
        int stepsPlanned,
        int stepsPerformed,
        double dtPhysStep,
        double dtPhysFrameReq,
        double dtPhysFrameMax,
        bool limited,
        bool limitedPhys,
        bool limitedCpu,
        bool step0)
    {
        if (statusHUD == null) return;

        statusHUD.SetStatus(new StatusHUD.Status
        {
            fps = (dtAnim > 1e-6f) ? (1.0f / dtAnim) : 0f,

            particleCount = particleCount,
            dueThisFrame = dueTotal,
            activePlanned = activePlanned,
            integratedThisFrame = integrated,
            missedThisFrame = missed,

            requestedSpeed = requestedSpeed,
            effectiveSpeed = effectiveSpeed,

            limited = limited,
            limitedByPhysics = limitedPhys,
            limitedByBudget = limitedCpu,
            limitedByBudgetStep0 = step0,
            pausedNoField = pausedNoField,

            budgetMs = budgetMs,
            usedMs = usedMs,

            stepsPlanned = stepsPlanned,
            stepsPerformed = stepsPerformed,

            dtPhysStep = dtPhysStep,
            dtPhysFrameReq = dtPhysFrameReq,
            dtPhysFrameMax = dtPhysFrameMax
        });
    }

    private bool ShouldLogThisFrame()
    {
        int n = Mathf.Max(1, debugLogIntervalFrames);
        return (frameIndex % n) == 0;
    }

    private void EmitPeriodicWarning(string msg)
    {
        if (!enableDebugLogs) return;
        if (!ShouldLogThisFrame()) return;
        UnityEngine.Debug.LogWarning(msg);
    }
}