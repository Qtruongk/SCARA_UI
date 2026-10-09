using System;
using System.Threading.Tasks;

namespace Test_1.Services
{
    public class MotionService
    {
        public event Action<double, double, double, double> OnDemoStep;
        public event Action OnDemoFinished;

        public async Task<(double finishTime, double xAxisMax)> StartTrapezoidalDemoAsync(double startPos, double distance, double vmax, double amax, double jmax = 0)
        {
            return await Task.Run(() =>
            {
                // 3-phase Trapezoidal times
                double t_accel = vmax / amax;
                double d_accel = 0.5 * amax * t_accel * t_accel;
                
                // If the distance is too short to reach vmax, we need a triangular profile
                if (distance < 2 * d_accel)
                {
                    d_accel = distance / 2;
                    t_accel = Math.Sqrt(2 * d_accel / amax);
                    vmax = amax * t_accel;
                }

                double d_const = distance - 2 * d_accel;
                double t_const = d_const / vmax;

                double t_decel = t_accel;

                double T1 = t_accel;
                double T2 = T1 + t_const;
                double T3 = T2 + t_decel;

                double xAxisMax = T3;
                if (jmax > 0)
                {
                    double t1_s = amax / jmax;
                    double v1_s = 0.5 * jmax * t1_s * t1_s;
                    double amax_s = amax;
                    if (vmax < 2 * v1_s) {
                        amax_s = Math.Sqrt(vmax * jmax);
                        t1_s = amax_s / jmax;
                        v1_s = 0.5 * jmax * t1_s * t1_s;
                    }
                    double t2_s = (vmax - 2 * v1_s) / amax_s;
                    double t4_s = (distance - 2 * (v1_s * t2_s + 0.5 * amax_s * t2_s * t2_s + v1_s * t1_s + 0.5 * amax_s * t1_s * t1_s)) / vmax; // Simplified estimation for max time
                    
                    // Actually, let's just properly estimate S-Curve total time
                    double s1 = (1.0/6.0) * jmax * t1_s * t1_s * t1_s;
                    double s2 = v1_s * t2_s + 0.5 * amax_s * t2_s * t2_s;
                    double v2_s = v1_s + amax_s * t2_s;
                    double s3 = v2_s * t1_s + 0.5 * amax_s * t1_s * t1_s - (1.0/6.0) * jmax * t1_s * t1_s * t1_s;
                    double s_accel = s1 + s2 + s3;
                    double s_const = distance - 2 * s_accel;
                    if (s_const < 0) s_const = 0;
                    double t_total_s = t1_s * 4 + t2_s * 2 + s_const / vmax;
                    
                    if (t_total_s > xAxisMax)
                    {
                        xAxisMax = t_total_s;
                    }
                }

                double t = 0;
                while (t <= T3)
                {
                    double p = 0, v = 0, a = 0;
                    if (t <= T1)
                    {
                        a = amax;
                        v = amax * t;
                        p = 0.5 * amax * t * t;
                    }
                    else if (t <= T2)
                    {
                        double dt = t - T1;
                        a = 0;
                        v = vmax;
                        p = d_accel + vmax * dt;
                    }
                    else if (t <= T3)
                    {
                        double dt = t - T2;
                        a = -amax;
                        v = vmax - amax * dt;
                        p = d_accel + d_const + vmax * dt - 0.5 * amax * dt * dt;
                    }
                    OnDemoStep?.Invoke(t, startPos + p, v, a);
                    
                    double next_t = t + T3 / 70.0;
                    if (t < T1 && next_t > T1) next_t = T1;
                    else if (t < T2 && next_t > T2) next_t = T2;
                    else if (t < T3 && next_t > T3) next_t = T3;
                    
                    t = next_t;
                    System.Threading.Thread.Sleep(40);
                }
                
                OnDemoStep?.Invoke(T3, startPos + distance, 0, 0);
                OnDemoFinished?.Invoke();
                return (T3, xAxisMax);
            });
        }

        public async Task<(double finishTime, double xAxisMax)> StartSCurveDemoAsync(double startPos, double distance, double vmax, double amax, double jmax)
        {
            return await Task.Run(() =>
            {
                // 7-phase S-curve times
                double t1 = amax / jmax;
                double v1 = 0.5 * jmax * t1 * t1;
                
                // Rào chắn 1: Vận tốc tối đa không đủ lớn để đạt gia tốc tối đa
                if (vmax < 2 * v1) {
                    amax = Math.Sqrt(vmax * jmax);
                    t1 = amax / jmax;
                    v1 = 0.5 * jmax * t1 * t1;
                }

                double t2 = (vmax - 2 * v1) / amax;
                double t3 = t1;
                
                double s1 = (1.0/6.0) * jmax * t1 * t1 * t1;
                double s2 = v1 * t2 + 0.5 * amax * t2 * t2;
                double v2 = v1 + amax * t2;
                double s3 = v2 * t3 + 0.5 * amax * t3 * t3 - (1.0/6.0) * jmax * t3 * t3 * t3;
                
                double s_accel = s1 + s2 + s3;
                double s_decel = s_accel;
                
                // Rào chắn 2: Quãng đường quá ngắn không đủ để đạt vmax
                if (distance < 2 * s_accel) {
                    throw new ArgumentException("Quãng đường quá ngắn để chạy S-Curve với tham số hiện tại.");
                }

                double s_const = distance - s_accel - s_decel;
                double t4 = s_const / vmax;
                
                double t5 = t3;
                double t6 = t2;
                double t7 = t1;
                
                double T1 = t1;
                double T2 = T1 + t2;
                double T3 = T2 + t3;
                double T4 = T3 + t4;
                double T5 = T4 + t5;
                double T6 = T5 + t6;
                double T7 = T6 + t7;

                double t = 0;
                while (t <= T7)
                {
                    double p = 0, v = 0, a = 0;
                    if (t <= T1) {
                        a = jmax * t;
                        v = 0.5 * jmax * t * t;
                        p = (1.0/6.0) * jmax * t * t * t;
                    } else if (t <= T2) {
                        double dt = t - T1;
                        a = amax;
                        v = v1 + amax * dt;
                        p = s1 + v1 * dt + 0.5 * amax * dt * dt;
                    } else if (t <= T3) {
                        double dt = t - T2;
                        a = amax - jmax * dt;
                        v = v2 + amax * dt - 0.5 * jmax * dt * dt;
                        p = s1 + s2 + v2 * dt + 0.5 * amax * dt * dt - (1.0/6.0) * jmax * dt * dt * dt;
                    } else if (t <= T4) {
                        double dt = t - T3;
                        a = 0;
                        v = vmax;
                        p = s_accel + vmax * dt;
                    } else if (t <= T5) {
                        double dt = t - T4;
                        a = -jmax * dt;
                        v = vmax - 0.5 * jmax * dt * dt;
                        p = s_accel + s_const + vmax * dt - (1.0/6.0) * jmax * dt * dt * dt;
                    } else if (t <= T6) {
                        double dt = t - T5;
                        double v5 = vmax - 0.5 * jmax * t5 * t5;
                        double s5 = vmax * t5 - (1.0/6.0) * jmax * t5 * t5 * t5;
                        a = -amax;
                        v = v5 - amax * dt;
                        p = s_accel + s_const + s5 + v5 * dt - 0.5 * amax * dt * dt;
                    } else {
                        double dt = t - T6;
                        double v5 = vmax - 0.5 * jmax * t5 * t5;
                        double s5 = vmax * t5 - (1.0/6.0) * jmax * t5 * t5 * t5;
                        double v6 = v5 - amax * t6;
                        double s6 = v5 * t6 - 0.5 * amax * t6 * t6;
                        a = -amax + jmax * dt;
                        v = v6 - amax * dt + 0.5 * jmax * dt * dt;
                        p = s_accel + s_const + s5 + s6 + v6 * dt - 0.5 * amax * dt * dt + (1.0/6.0) * jmax * dt * dt * dt;
                    }
                    OnDemoStep?.Invoke(t, startPos + p, v, a);
                    
                    double next_t = t + T7 / 70.0;
                    if (t < T1 && next_t > T1) next_t = T1;
                    else if (t < T2 && next_t > T2) next_t = T2;
                    else if (t < T3 && next_t > T3) next_t = T3;
                    else if (t < T4 && next_t > T4) next_t = T4;
                    else if (t < T5 && next_t > T5) next_t = T5;
                    else if (t < T6 && next_t > T6) next_t = T6;
                    else if (t < T7 && next_t > T7) next_t = T7;
                    
                    t = next_t;
                    System.Threading.Thread.Sleep(40);
                }
                
                OnDemoStep?.Invoke(T7, startPos + distance, 0, 0);
                OnDemoFinished?.Invoke();
                return (T7, T7);
            });
        }
    }
}
