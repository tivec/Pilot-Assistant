﻿using System;
using UnityEngine;

namespace PilotAssistant
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class PilotAssistant : MonoBehaviour
    {        
        private Vessel thisVessel = null;
        private PID.PID_Controller HeadingController = new PID.PID_Controller(3, 0.1, 0, -30, 30, -0.1, 0.1);
        private PID.PID_Controller RollController = new PID.PID_Controller(0.01, 0.01, 0.01, -1, 1, -0.1, 0.1);

        private PID.PID_Controller AltitudeToClimbRate = new PID.PID_Controller(0.1, 0, 0, -30, 30, -1, 1); // P control for converting altitude hold to climb rate
        private PID.PID_Controller PitchController = new PID.PID_Controller(3, 0.4, 1.5, -10, 10, -10, 10); // Input craft altitude, output target craft pitch
        private PID.PID_Controller ElevatorController = new PID.PID_Controller(0.01, 0.01, 0.01, -1, 1, -0.1, 0.1); // Convert pitch input to control surface deflection

        private PID.PID_Controller YawController = new PID.PID_Controller(0.05, 0.01, 0.1, -1, 1, -0.1, 0.1);

        private Rect window = new Rect(10, 50, 500, 500);
        // RollController
        private bool rollActive = false;
        private bool rollWasActive = false;
        // PitchController
        private bool pitchActive = false;
        private bool pitchWasActive = false;
        private bool bAltitudeHold = true;

        private double pitch = 0, roll = 0, yaw = 0, AoA = 0, heading = 0; // currenct craft attitude variables

        private string targetAltitude = "0";
        private string targetSpeed = "0";
        private string targetHeading = "0";

        private bool showPIDGains = false;
        private bool showPIDLimits = false;

        public void Start()
        {
            thisVessel = FlightGlobals.ActiveVessel;
            thisVessel.OnFlyByWire += new FlightInputCallback(vesselController);

            RollController.InMax = 180;
            RollController.InMin = -180;

            AltitudeToClimbRate.InMin = 0;
        }

        public void Update()
        {
            if (rollActive && !rollWasActive)
            {
                rollWasActive = true;
                HeadingController.SetPoint = heading;
                targetHeading = heading.ToString("N2");
            }
            if (!rollActive && rollWasActive)
            {
                rollWasActive = false;
                HeadingController.Clear();
                RollController.Clear();
            }

            if (pitchActive && !pitchWasActive)
            {
                pitchWasActive = true;
                if (bAltitudeHold)
                    targetAltitude = thisVessel.altitude.ToString("N1");
                AltitudeToClimbRate.SetPoint = thisVessel.altitude;
            }
            if (!pitchActive && pitchWasActive)
            {
                pitchWasActive = false;
                AltitudeToClimbRate.Clear();
                PitchController.Clear();
                ElevatorController.Clear();
            }
        }

        public void FixedUpdate()
        {
        }

        public void OnGUI()
        {
            if (!bDisplay)
                return;

            window = GUI.Window(34244, window, displayWindow, "");
        }

        private void displayWindow(int id)
        {
            string text;
            GUILayout.BeginVertical();

            #region Roll GUI
            // Roll Control
            GUILayout.Label("Heading Hold");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Target Heading: ", GUILayout.Width(100));
            //
            targetHeading = GUILayout.TextField(targetHeading, GUILayout.Width(100));
            if (GUILayout.Button("Update Target Heading", GUILayout.Width(150)))
            {
                double newHdg;
                double.TryParse(targetHeading, out newHdg);
                if (newHdg >= 0 && newHdg <= 360)
                {
                    HeadingController.SetPoint = newHdg;
                    rollActive = true;
                }
            }
            //
            GUILayout.Space(20);
            GUILayout.Label("Current Heading: " + heading.ToString("N2") + "\u00B0", GUILayout.Width(250));
            GUILayout.EndHorizontal();

            #region Heading Controller
            if (showPIDGains)
            {
                GUILayout.BeginHorizontal();

                GUILayout.Label("Hdg Kp: ", GUILayout.Width(80));
                text = GUILayout.TextField(HeadingController.PGain.ToString(), GUILayout.Width(60));
                HeadingController.PGain = double.Parse(text);

                GUILayout.Space(20);

                GUILayout.Label("Hdg Ki: ", GUILayout.Width(80));
                text = GUILayout.TextField(HeadingController.IGain.ToString(), GUILayout.Width(60));
                HeadingController.IGain = double.Parse(text);

                GUILayout.Space(20);

                GUILayout.Label("Hdg Kd: ", GUILayout.Width(80));
                text = GUILayout.TextField(HeadingController.DGain.ToString(), GUILayout.Width(60));
                HeadingController.DGain = double.Parse(text);

                if (showPIDLimits)
                {
                    GUILayout.Space(5);

                    GUILayout.Label("Hdg Min Out: ", GUILayout.Width(120));
                    text = GUILayout.TextField(HeadingController.OutMin.ToString(), GUILayout.Width(60));
                    HeadingController.OutMin = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Hdg Max Out: ", GUILayout.Width(120));
                    text = GUILayout.TextField(HeadingController.OutMax.ToString(), GUILayout.Width(60));
                    HeadingController.OutMax = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Hdg Clamp Lower: ", GUILayout.Width(120));
                    text = GUILayout.TextField(HeadingController.ClampLower.ToString(), GUILayout.Width(60));
                    HeadingController.ClampLower = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Hdg Clamp Upper: ", GUILayout.Width(120));
                    text = GUILayout.TextField(HeadingController.ClampUpper.ToString(), GUILayout.Width(60));
                    HeadingController.ClampUpper = double.Parse(text);
                }
                GUILayout.EndHorizontal();
            }
            #endregion

            GUILayout.Label("Target Angle: " + RollController.SetPoint.ToString("N3") + "\u00B0", GUILayout.Width(250));

            #region Roll Controller
            if (showPIDGains)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Roll Kp: ", GUILayout.Width(80));
                text = GUILayout.TextField(RollController.PGain.ToString(), GUILayout.Width(60));
                RollController.PGain = double.Parse(text);

                GUILayout.Space(20);

                GUILayout.Label("Roll Ki: ", GUILayout.Width(80));
                text = GUILayout.TextField(RollController.IGain.ToString(), GUILayout.Width(60));
                RollController.IGain = double.Parse(text);

                GUILayout.Space(20);

                GUILayout.Label("Roll Kd: ", GUILayout.Width(80));
                text = GUILayout.TextField(RollController.DGain.ToString(), GUILayout.Width(60));
                RollController.DGain = double.Parse(text);

                if (showPIDLimits)
                {
                    GUILayout.Space(5);

                    GUILayout.Label("Roll Min Out: ", GUILayout.Width(120));
                    text = GUILayout.TextField(RollController.OutMin.ToString(), GUILayout.Width(60));
                    RollController.OutMin = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Roll Max Out: ", GUILayout.Width(120));
                    text = GUILayout.TextField(RollController.OutMax.ToString(), GUILayout.Width(60));
                    RollController.OutMax = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Roll Clamp Lower: ", GUILayout.Width(120));
                    text = GUILayout.TextField(RollController.ClampLower.ToString(), GUILayout.Width(60));
                    RollController.ClampLower = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Roll Clamp Upper: ", GUILayout.Width(120));
                    text = GUILayout.TextField(RollController.ClampUpper.ToString(), GUILayout.Width(60));
                    RollController.ClampUpper = double.Parse(text);
                }
                GUILayout.EndHorizontal();
            }
            #endregion
            rollActive = GUILayout.Toggle(rollActive, "Heading Lock Active", GUILayout.Width(200));
            #endregion

            #region Pitch GUI
            //// Pitch Controls
            GUILayout.Label("", GUILayout.Height(10)); // vertical spacer
            GUILayout.Label("Vertical Control");
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Target Altitude: ", GUILayout.Width(100));
            //
            targetAltitude = GUILayout.TextField(targetAltitude, GUILayout.Width(100));
            if (GUILayout.Button("Update Target Altitude", GUILayout.Width(150)))
            {
                double newAlt;
                double.TryParse(targetAltitude, out newAlt);
                if (newAlt > 0)
                {
                    AltitudeToClimbRate.SetPoint = newAlt;
                    bAltitudeHold = true;
                    pitchActive = true;
                }
            }
            //
            GUILayout.Space(20);
            GUILayout.Label("Current Altitude: " + thisVessel.altitude.ToString("N1") + "m", GUILayout.Width(250));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Target Speed: ", GUILayout.Width(100));
            //
            targetSpeed = GUILayout.TextField(targetSpeed, GUILayout.Width(100));
            if (GUILayout.Button("Update Target Speed", GUILayout.Width(150)))
            {
                double newSpeed;
                double.TryParse(targetSpeed, out newSpeed);
                if (newSpeed != 0)
                {
                    PitchController.SetPoint = newSpeed;
                    bAltitudeHold = false;
                    pitchActive = true;
                }
            }
            //
            GUILayout.Space(20);
            GUILayout.Label("Current Speed: " + thisVessel.verticalSpeed.ToString("N3") + "m/s", GUILayout.Width(250));
            GUILayout.EndHorizontal();

            bAltitudeHold = GUILayout.Toggle(bAltitudeHold, bAltitudeHold ? "Mode: Altitude" : "Mode: Vertical Speed", GUILayout.Width(200));

            if (showPIDGains)
            {
                #region Altitude Controller
                if (bAltitudeHold)
                {
                    GUILayout.BeginHorizontal();

                    GUILayout.Label("Altitude Kp: ", GUILayout.Width(80));
                    text = GUILayout.TextField(AltitudeToClimbRate.PGain.ToString(), GUILayout.Width(60));
                    AltitudeToClimbRate.PGain = double.Parse(text);

                    GUILayout.Space(20);

                    GUILayout.Label("Altitude Ki: ", GUILayout.Width(80));
                    text = GUILayout.TextField(AltitudeToClimbRate.IGain.ToString(), GUILayout.Width(60));
                    AltitudeToClimbRate.IGain = double.Parse(text);

                    GUILayout.Space(20);

                    GUILayout.Label("Altitude Kd: ", GUILayout.Width(80));
                    text = GUILayout.TextField(AltitudeToClimbRate.DGain.ToString(), GUILayout.Width(60));
                    AltitudeToClimbRate.DGain = double.Parse(text);

                    if (showPIDLimits)
                    {
                        GUILayout.Space(5);

                        GUILayout.Label("Altitude Min Out: ", GUILayout.Width(120));
                        text = GUILayout.TextField((-AltitudeToClimbRate.OutMax).ToString(), GUILayout.Width(60));
                        AltitudeToClimbRate.OutMax = -1 * double.Parse(text);

                        GUILayout.Space(5);

                        GUILayout.Label("Altitude Max Out: ", GUILayout.Width(120));
                        text = GUILayout.TextField((-AltitudeToClimbRate.OutMin).ToString(), GUILayout.Width(60));
                        AltitudeToClimbRate.OutMin = -1 * double.Parse(text);

                        GUILayout.Space(5);

                        GUILayout.Label("Clamp Lower: ", GUILayout.Width(120));
                        text = GUILayout.TextField(AltitudeToClimbRate.ClampLower.ToString(), GUILayout.Width(60));
                        AltitudeToClimbRate.ClampLower = double.Parse(text);

                        GUILayout.Space(5);

                        GUILayout.Label("Clamp Upper: ", GUILayout.Width(120));
                        text = GUILayout.TextField(AltitudeToClimbRate.ClampUpper.ToString(), GUILayout.Width(60));
                        AltitudeToClimbRate.ClampUpper = double.Parse(text);
                    }

                    GUILayout.EndHorizontal();
                }
                #endregion

                #region AoA Controller
                GUILayout.BeginHorizontal();

                GUILayout.Label("AoA Kp: ", GUILayout.Width(80));
                text = GUILayout.TextField(PitchController.PGain.ToString(), GUILayout.Width(60));
                PitchController.PGain = double.Parse(text);

                GUILayout.Space(20);

                GUILayout.Label("AoA Ki: ", GUILayout.Width(80));
                text = GUILayout.TextField(PitchController.IGain.ToString(), GUILayout.Width(60));
                PitchController.IGain = double.Parse(text);

                GUILayout.Space(20);

                GUILayout.Label("AoA Kd: ", GUILayout.Width(80));
                text = GUILayout.TextField(PitchController.DGain.ToString(), GUILayout.Width(60));
                PitchController.DGain = double.Parse(text);

                if (showPIDLimits)
                {
                    GUILayout.Space(5);

                    GUILayout.Label("AoA Min Out: ", GUILayout.Width(120));
                    text = GUILayout.TextField((-PitchController.OutMax).ToString(), GUILayout.Width(60));
                    PitchController.OutMax = -1 * double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("AoA Max Out: ", GUILayout.Width(120));
                    text = GUILayout.TextField((-PitchController.OutMin).ToString(), GUILayout.Width(60));
                    PitchController.OutMin = -1 * double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("AoA Clamp Lower: ", GUILayout.Width(120));
                    text = GUILayout.TextField(PitchController.ClampLower.ToString(), GUILayout.Width(60));
                    PitchController.ClampLower = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("AoA Clamp Upper: ", GUILayout.Width(120));
                    text = GUILayout.TextField(PitchController.ClampUpper.ToString(), GUILayout.Width(60));
                    PitchController.ClampUpper = double.Parse(text);
                }
                GUILayout.EndHorizontal();
                #endregion

                #region Elevator Controller
                GUILayout.BeginHorizontal();

                GUILayout.Label("Elev Kp: ", GUILayout.Width(80));
                text = GUILayout.TextField(ElevatorController.PGain.ToString(), GUILayout.Width(60));
                ElevatorController.PGain = double.Parse(text);

                GUILayout.Space(20);

                GUILayout.Label("Elev Ki: ", GUILayout.Width(80));
                text = GUILayout.TextField(ElevatorController.IGain.ToString(), GUILayout.Width(60));
                ElevatorController.IGain = double.Parse(text);

                GUILayout.Space(20);

                GUILayout.Label("Elev Kd: ", GUILayout.Width(80));
                text = GUILayout.TextField(ElevatorController.DGain.ToString(), GUILayout.Width(60));
                ElevatorController.DGain = double.Parse(text);

                if (showPIDLimits)
                {
                    GUILayout.Space(5);

                    GUILayout.Label("Elev Min Out: ", GUILayout.Width(120));
                    text = GUILayout.TextField(ElevatorController.OutMin.ToString(), GUILayout.Width(60));
                    ElevatorController.OutMin = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Elev Max Out: ", GUILayout.Width(120));
                    text = GUILayout.TextField(ElevatorController.OutMax.ToString(), GUILayout.Width(60));
                    ElevatorController.OutMax = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Elev Clamp Lower: ", GUILayout.Width(120));
                    text = GUILayout.TextField(ElevatorController.ClampLower.ToString(), GUILayout.Width(60));
                    ElevatorController.ClampLower = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Elev Clamp Upper ", GUILayout.Width(120));
                    text = GUILayout.TextField(ElevatorController.ClampUpper.ToString(), GUILayout.Width(60));
                    ElevatorController.ClampUpper = double.Parse(text);
                }
                GUILayout.EndHorizontal();
                #endregion
            }

            pitchActive = GUILayout.Toggle(pitchActive, "Pitch Control Active", GUILayout.Width(200));
            #endregion

            #region Yaw damper
            GUILayout.Label("", GUILayout.Height(20)); // spacer
            GUILayout.Label("Yaw Damper", GUILayout.Width(150));

            if (showPIDGains)
            {
                GUILayout.BeginHorizontal();

                GUILayout.Label("Yaw Kp: ", GUILayout.Width(80));
                text = GUILayout.TextField(YawController.PGain.ToString(), GUILayout.Width(60));
                YawController.PGain = double.Parse(text);

                GUILayout.Space(20);

                GUILayout.Label("Yaw Ki: ", GUILayout.Width(80));
                text = GUILayout.TextField(YawController.IGain.ToString(), GUILayout.Width(60));
                YawController.IGain = double.Parse(text);

                GUILayout.Space(20);

                GUILayout.Label("Yaw Kd: ", GUILayout.Width(80));
                text = GUILayout.TextField(YawController.DGain.ToString(), GUILayout.Width(60));
                YawController.DGain = double.Parse(text);

                if (showPIDLimits)
                {
                    GUILayout.Space(5);

                    GUILayout.Label("Yaw Min Out: ", GUILayout.Width(120));
                    text = GUILayout.TextField(YawController.OutMin.ToString(), GUILayout.Width(60));
                    YawController.OutMin = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Yaw Max Out: ", GUILayout.Width(120));
                    text = GUILayout.TextField(YawController.OutMax.ToString(), GUILayout.Width(60));
                    YawController.OutMax = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Yaw Clamp Lower: ", GUILayout.Width(120));
                    text = GUILayout.TextField(YawController.ClampLower.ToString(), GUILayout.Width(60));
                    YawController.ClampLower = double.Parse(text);

                    GUILayout.Space(5);

                    GUILayout.Label("Yaw Clamp Upper ", GUILayout.Width(120));
                    text = GUILayout.TextField(YawController.ClampUpper.ToString(), GUILayout.Width(60));
                    YawController.ClampUpper = double.Parse(text);
                }
                GUILayout.EndHorizontal();
            }
            bool yawActive = GUILayout.Toggle(pitchActive || rollActive, "Yaw Damper Active", GUILayout.Width(200));
            #endregion

            GUILayout.Label("");
            showPIDGains = GUILayout.Toggle(showPIDGains, "Show PID Gains", GUILayout.Width(200));
            if (showPIDGains)
            {
                showPIDLimits = GUILayout.Toggle(showPIDLimits, "Show PID Limits", GUILayout.Width(200));
                window.height = 550;
                if (showPIDLimits)
                {
                    window.width = 1300;
                }
                else
                {
                    window.width = 600;
                }
            }
            else
            {
                window.width = 600;
                window.height = 375;
            }
            GUILayout.EndVertical();



            GUI.DragWindow();
        }

        private void vesselController(FlightCtrlState state)
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;
            if (thisVessel == null)
                return;

            updateAttitude();
            
            // Heading Control
            if (rollActive)
            {
                // Fix heading so it behaves properly traversing 0/360 degrees
                if (HeadingController.SetPoint - heading >= -180 && HeadingController.SetPoint - heading <= 180)
                    RollController.SetPoint = HeadingController.Response(heading);
                else if (HeadingController.SetPoint - heading < -180)
                    RollController.SetPoint = HeadingController.Response(heading - 360);
                else if (HeadingController.SetPoint - heading > 180)
                    RollController.SetPoint = HeadingController.Response(heading + 360);

                state.roll = (float)Clamp(RollController.Response(roll) + state.roll, -1, 1);
            }

            // Pitch Controller
            // Work on vertical speed, altitude hold can use a proportional error as input
            if (pitchActive)
            {
                // Set requested vertical speed
                if (bAltitudeHold)
                    PitchController.SetPoint = -AltitudeToClimbRate.Response(thisVessel.altitude);

                ElevatorController.SetPoint = -PitchController.Response(thisVessel.verticalSpeed);
                state.pitch = (float)Clamp(-ElevatorController.Response(AoA) + state.pitch, -1, 1);
            }

            if (rollActive || pitchActive)
            {
                print("Yaw: " + yaw.ToString());
                state.yaw = (float)Clamp(YawController.Response(yaw) + state.yaw, -1, 1);
            }
        }


        private NavBall ball;
        private void updateAttitude()
        {
            // blatant copying of FAR get attitude logic because its just so straightfoward...
            if (ball == null)
                ball = FlightUIController.fetch.GetComponentInChildren<NavBall>();

            // pitch/roll/heading
            Quaternion vesselRot = Quaternion.Inverse(ball.relativeGymbal);
            pitch = (vesselRot.eulerAngles.x > 180) ? (360 - vesselRot.eulerAngles.x) : -vesselRot.eulerAngles.x; // pitch up is +ve
            roll = (vesselRot.eulerAngles.z > 180) ? (vesselRot.eulerAngles.z - 360) : vesselRot.eulerAngles.z;
            heading = vesselRot.eulerAngles.y;

            // pitch AoA
            Vector3 tmpVec = thisVessel.ReferenceTransform.up * Vector3.Dot(thisVessel.ReferenceTransform.up, thisVessel.srf_velocity.normalized) + thisVessel.ReferenceTransform.forward * Vector3.Dot(thisVessel.ReferenceTransform.forward, thisVessel.srf_velocity.normalized);   //velocity vector projected onto a plane that divides the airplane into left and right halves
            AoA = Vector3.Dot(tmpVec.normalized, thisVessel.ReferenceTransform.forward);
            AoA = 180 / Math.PI * Math.Asin(AoA);
            if (double.IsNaN(AoA))
                AoA = 0;

            // yaw AoA
            tmpVec = thisVessel.ReferenceTransform.up * Vector3.Dot(thisVessel.ReferenceTransform.up, thisVessel.srf_velocity.normalized) + thisVessel.ReferenceTransform.right * Vector3.Dot(thisVessel.ReferenceTransform.right, thisVessel.srf_velocity.normalized);     //velocity vector projected onto the vehicle-horizontal plane
            yaw = Vector3.Dot(tmpVec.normalized, thisVessel.ReferenceTransform.right);
            yaw = 180 / Math.PI * Math.Asin(yaw);
            if (double.IsNaN(yaw))
                yaw = 0;
        }

        #region Applauncher Functions and Variables

        private ApplicationLauncherButton btnLauncher;
        private bool bDisplay = false;

        private void Awake()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(this.OnAppLauncherReady);
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(this.OnAppLauncherReady);
            if (btnLauncher != null)
                ApplicationLauncher.Instance.RemoveModApplication(this.btnLauncher);
        }

        private void OnAppLauncherReady()
        {
            btnLauncher = ApplicationLauncher.Instance.AddModApplication(OnToggleTrue, OnToggleFalse,
                                                                        null, null, null, null,
                                                                        ApplicationLauncher.AppScenes.ALWAYS,
                                                                        GameDatabase.Instance.GetTexture("PilotAssistant/Icons/AppLauncherIcon", false));
        }

        private void OnGameSceneChange(GameScenes scene)
        {
            bDisplay = false;
            ApplicationLauncher.Instance.RemoveModApplication(btnLauncher);
        }

        private void OnToggleTrue()
        {
            bDisplay = true;
        }

        private void OnToggleFalse()
        {
            bDisplay = false;
        }
        #endregion

        #region Utility Functions

        /// <summary>
        /// Clamp double input between maximum and minimum value
        /// </summary>
        /// <param name="val">variable to be clamped</param>
        /// <param name="min">minimum output value of the variable</param>
        /// <param name="max">maximum output value of the variable</param>
        /// <returns>val clamped between max and min</returns>
        internal static double Clamp(double val, double min, double max)
        {
            if (val < min)
                return min;
            else if (val > max)
                return max;
            else
                return val;
        }

        /// <summary>
        /// Linear interpolation between two points
        /// </summary>
        /// <param name="pct">fraction of travel from the minimum to maximum. Can be less than 0 or greater than 1</param>
        /// <param name="lower">reference point treated as the base (pct = 0)</param>
        /// <param name="upper">reference point treated as the target (pct = 1)</param>
        /// <param name="clamp">clamp pct input between 0 and 1?</param>
        /// <returns></returns>
        internal static double Lerp(double pct, double lower, double upper, bool clamp = true)
        {
            if (clamp)
            {
                pct = Clamp(pct, 0, 1);
            }
            return (1 - pct) * lower + pct * upper;
        }

        #endregion
    }
}
