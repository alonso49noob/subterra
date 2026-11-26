using System;
using System.Collections.Generic;
using Expansions.Serenity;
using ModuleWheels;
using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Stock parts made up for the thick air and water deep down (Lucas: wheels, reaction wheels and engines could not work
    /// under those pressures; only when very deep; propellers and intakes yes, rocket engines no). Past
    /// <see cref="MinAtm"/> of pressure round a craft under the ground or in an ocean, the torque of wheel motors, reaction
    /// wheels and rotors, and the thrust, fuel flow and intake area of air-breathing engines grow with the square root of the
    /// pressure (at most <see cref="MaxBoost"/> times): the drag grows with the air's density, and without it a rover or a
    /// jet could not move. Put back as they were above. [Claude, revisar]
    /// </summary>
    public sealed class DeepAssistModule : VesselModule
    {
        /// <summary>Pressure (atm) from which parts are made up for it, and the most they are multiplied by.</summary>
        public const double MinAtm = 5, MaxBoost = 25;

        private readonly Dictionary<PartModule, float[]> original = new Dictionary<PartModule, float[]>();
        private readonly Dictionary<ModuleWheelMotor, FloatCurve> curves = new Dictionary<ModuleWheelMotor, FloatCurve>();
        private float applied = 1, nextCheck;

        public override Activation GetActivation() => Activation.LoadedVessels | Activation.FlightScene;

        /// <summary>The multiplier for <paramref name="atm"/> of pressure deep down (1 under <see cref="MinAtm"/>).</summary>
        public static float Boost(double atm) => atm < MinAtm ? 1f : (float)Math.Min(MaxBoost, Math.Sqrt(atm));

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded || Time.time < nextCheck)
            {
                return;
            }

            nextCheck = Time.time + 1f;
            float boost = 1;
            if (Deep())
            {
                double kPa = vessel.staticPressurekPa;
                SubterraWaterModule water = SubterraWaterModule.Of(vessel);
                if (water != null && water.PressureOn(vessel.rootPart, out double wet))
                {
                    kPa = Math.Max(kPa, wet);
                }

                boost = Boost(kPa / 101.325);
            }

            if (Math.Abs(boost - applied) < 0.05f * applied && original.Count > 0 == boost > 1)
            {
                return;
            }

            Apply(boost);
        }

        // Under the ground (in a cave, or buried) or in an ocean under the ice.
        private bool Deep()
        {
            if (vessel.mainBody == null || !BodyRegistry.Instance.TryGet(vessel.mainBody, out _))
            {
                return false;
            }

            return UndergroundTracker.For(vessel).Inside || ThermalPatches.RockTempAround(vessel).HasValue
                || Underground.OceanAt(vessel.mainBody, vessel.CoMD) is Core.Water.WaterSample s && s.Under;
        }

        private void Apply(float boost)
        {
            Restore();
            applied = boost;
            if (boost <= 1)
            {
                return;
            }

            foreach (Part p in vessel.parts)
            {
                foreach (PartModule m in p.Modules)
                {
                    switch (m)
                    {
                        case ModuleReactionWheel rw:
                            original[rw] = new[] { rw.PitchTorque, rw.YawTorque, rw.RollTorque };
                            rw.PitchTorque *= boost;
                            rw.YawTorque *= boost;
                            rw.RollTorque *= boost;
                            break;
                        case ModuleWheelMotor motor when motor.torqueCurve != null:
                            curves[motor] = motor.torqueCurve;
                            motor.torqueCurve = Scaled(motor.torqueCurve, boost);
                            original[motor] = new float[0];
                            break;
                        case ModuleRoboticServoRotor rotor:
                            original[rotor] = new[] { rotor.maxTorque };
                            rotor.maxTorque *= boost;
                            break;
                        case ModuleEngines engine when AirBreathing(engine):
                            original[engine] = new[] { engine.maxThrust, (float)engine.maxFuelFlow };
                            engine.maxThrust *= boost;
                            engine.maxFuelFlow *= boost;
                            break;
                        case ModuleResourceIntake intake:
                            original[intake] = new[] { (float)intake.area };
                            intake.area *= boost;
                            break;
                    }
                }
            }

            Log.Info($"Deep assist on {vessel.GetDisplayName()}: x{boost:F1} for {original.Count} part modules ({vessel.staticPressurekPa / 101.325:F0} atm)");
        }

        // Jets and other engines that breathe the air (not rockets).
        private static bool AirBreathing(ModuleEngines engine)
        {
            if (engine.engineType == EngineType.Turbine || engine.engineType == EngineType.Piston || engine.engineType == EngineType.ScramJet)
            {
                return true;
            }

            foreach (Propellant prop in engine.propellants)
            {
                if (prop.name == "IntakeAir" || prop.name == "IntakeAtm")
                {
                    return true;
                }
            }

            return false;
        }

        private static FloatCurve Scaled(FloatCurve curve, float k)
        {
            var scaled = new FloatCurve();
            foreach (Keyframe key in curve.Curve.keys)
            {
                scaled.Add(key.time, key.value * k, key.inTangent * k, key.outTangent * k);
            }

            return scaled;
        }

        private void Restore()
        {
            foreach (KeyValuePair<PartModule, float[]> pair in original)
            {
                float[] v = pair.Value;
                switch (pair.Key)
                {
                    case ModuleReactionWheel rw when rw != null:
                        rw.PitchTorque = v[0];
                        rw.YawTorque = v[1];
                        rw.RollTorque = v[2];
                        break;
                    case ModuleWheelMotor motor when motor != null && curves.TryGetValue(motor, out FloatCurve c):
                        motor.torqueCurve = c;
                        break;
                    case ModuleRoboticServoRotor rotor when rotor != null:
                        rotor.maxTorque = v[0];
                        break;
                    case ModuleEngines engine when engine != null:
                        engine.maxThrust = v[0];
                        engine.maxFuelFlow = v[1];
                        break;
                    case ModuleResourceIntake intake when intake != null:
                        intake.area = v[0];
                        break;
                }
            }

            original.Clear();
            curves.Clear();
            applied = 1;
        }

        // Saved as they were (the persistent fields of these modules are not the ones changed, but to be safe).
        private void OnDestroy() => Restore();
    }
}
