// ParticleIntegratorRelativisticBorisU.cs
using UnityEngine;

public static class ParticleIntegratorRelativisticBorisU
{
    // Relativistic Boris integrator storing u = gamma*v [m/s].
    // E [V/m], B [T], dt [s], q [C], m [kg], c [m/s]
    public static void Step(ref ParticleState s, Vector3 E, Vector3 B, float dt, float c)
    {
        float q = s.charge;
        float m = s.mass;
        float qOverM = q / m;
        float halfDt = 0.5f * dt;

        // u- = u + (q/m) E dt/2
        Vector3 uMinus = s.u + qOverM * E * halfDt;

        // gamma- = sqrt(1 + |u-|^2 / c^2)
        float invC2 = 1.0f / (c * c);
        float uMinus2 = Vector3.Dot(uMinus, uMinus);
        float gammaMinus = Mathf.Sqrt(1.0f + uMinus2 * invC2);

        // t = (q/m) B dt/2 / gamma-
        Vector3 t = (qOverM * halfDt / gammaMinus) * B;
        float t2 = Vector3.Dot(t, t);

        // sVec = 2t / (1 + t^2)
        Vector3 sVec = (2.0f / (1.0f + t2)) * t;

        // u' = u- + u- x t
        Vector3 uPrime = uMinus + Vector3.Cross(uMinus, t);

        // u+ = u- + u' x s
        Vector3 uPlus = uMinus + Vector3.Cross(uPrime, sVec);

        // u_new = u+ + (q/m) E dt/2
        Vector3 uNew = uPlus + qOverM * E * halfDt;

        // gamma_new = sqrt(1 + |u_new|^2 / c^2)
        float uNew2 = Vector3.Dot(uNew, uNew);
        float gammaNew = Mathf.Sqrt(1.0f + uNew2 * invC2);

        // v_new = u_new / gamma_new
        Vector3 vNew = uNew / gammaNew;

        // x_new = x + v_new dt
        s.position += vNew * dt;

        // store u
        s.u = uNew;
    }

    public static Vector3 VelocityFromU(Vector3 u, float c)
    {
        float invC2 = 1.0f / (c * c);
        float u2 = Vector3.Dot(u, u);
        float gamma = Mathf.Sqrt(1.0f + u2 * invC2);
        return u / gamma;
    }

    public static float SpeedFromU(Vector3 u, float c)
    {
        float invC2 = 1.0f / (c * c);
        float u2 = Vector3.Dot(u, u);
        float gamma = Mathf.Sqrt(1.0f + u2 * invC2);
        return (u / gamma).magnitude;
    }

    public static Vector3 UFromV(Vector3 v, float c)
    {
        float invC2 = 1.0f / (c * c);
        float v2 = Vector3.Dot(v, v);
        float beta2 = Mathf.Clamp(v2 * invC2, 0f, 0.999999f);
        float gamma = 1.0f / Mathf.Sqrt(1.0f - beta2);
        return gamma * v;
    }
}