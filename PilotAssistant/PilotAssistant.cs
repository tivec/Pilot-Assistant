﻿using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine;
using KSP.IO;

namespace PilotAssistant
{
    using Presets;
    using Utility;
    using PID;

    public enum PIDList
    {
        HdgBank,
        BankToYaw,
        Aileron,
        Rudder,
        Altitude,
        VertSpeed,
        Elevator,
        Throttle
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class PilotAssistant : MonoBehaviour
    {
        private static PilotAssistant instance;
        public static PilotAssistant Instance 
        {
            get { return instance; }
        }

        static bool init = false; // create the default the first time through
        public static PID_Controller[] controllers = new PID_Controller[8];

        bool bPause = false;

        // RollController
        public bool bHdgActive = false;
        bool bHdgWasActive = false;
        // PitchController
        public bool bVertActive = false;
        bool bVertWasActive = false;
        // Altitude / vertical speed
        public bool bAltitudeHold = false;
        bool bWasAltitudeHold = false;
        // Wing leveller / Heading control
        public bool bWingLeveller = false;
        bool bWasWingLeveller = false;
        // Throttle control
        public bool bThrottleActive = false;
        bool bThrottleWasActive = false;

        public Rect window = new Rect(10, 130, 10, 10);

        Vector2 scrollbarHdg = Vector2.zero;
        Vector2 scrollbarVert = Vector2.zero;

        public bool showPresets = false;
        public bool showPIDLimits = false;
        public bool showControlSurfaces = false;
        public bool doublesided = false;
        public bool showTooltips = true;

        string targetVert = "0.00";
        string targetHeading = "0.00";
        string targetSpeed = "0.00";

        bool headingEdit = false;

        bool bShowSettings = false;
        bool bShowHdg = true;
        bool bShowVert = true;
        bool bShowThrottle = true;

        float hdgScrollHeight;
        float vertScrollHeight;

        string newPresetName = "";
        Rect presetWindow = new Rect(0, 0, 200, 10);

        public static double[] defaultHdgBankGains = { 2, 0.1, 0, -30, 30, -0.5, 0.5, 1, 1 };
        public static double[] defaultBankToYawGains = { 0, 0, 0.01, -2, 2, -0.5, 0.5, 1, 1 };
        public static double[] defaultAileronGains = { 0.02, 0.005, 0.01, -1, 1, -0.4, 0.4, 1, 1 };
        public static double[] defaultRudderGains = { 0.1, 0.08, 0.05, -1, 1, -0.4, 0.4, 1, 1 };
        public static double[] defaultAltitudeGains = { 0.15, 0.01, 0, -50, 50, -0.01, 0.01, 1, 100 };
        public static double[] defaultVSpeedGains = { 2, 0.8, 2, -10, 10, -5, 5, 1, 10 };
        public static double[] defaultElevatorGains = { 0.05, 0.01, 0.1, -1, 1, -0.4, 0.4, 1, 1 };
        public static double[] defaultThrottleGains = { 0.2, 0.08, 0.1, -1, 0, -1, 0.4, 1, 1 };

        Vector3 axisLock = new Vector3();

        public void Start()
        {
            instance = this;
            
            if (!init)
                Initialise();

            // register vessel
            if (FlightData.thisVessel == null)
                FlightData.thisVessel = FlightGlobals.ActiveVessel;

            PresetManager.loadCraftAsstPreset();

            PIDList.Aileron.GetAsst().InMax = 180;
            PIDList.Aileron.GetAsst().InMin = -180;
            PIDList.Altitude.GetAsst().InMin = 0;
            PIDList.Throttle.GetAsst().InMin = 0;

            FlightData.thisVessel.OnPreAutopilotUpdate += new FlightInputCallback(preAutoPilotEvent);
            FlightData.thisVessel.OnPostAutopilotUpdate += new FlightInputCallback(vesselController);
            GameEvents.onVesselChange.Add(vesselSwitch);
            GameEvents.onTimeWarpRateChanged.Add(warpHandler);

            RenderingManager.AddToPostDrawQueue(5, drawGUI);

            StartCoroutine(headingKeyboardResponse());
        }

        void Initialise()
        {
            controllers[(int)PIDList.HdgBank] = new PID_Controller(defaultHdgBankGains);
            controllers[(int)PIDList.BankToYaw] = new PID_Controller(defaultBankToYawGains);
            controllers[(int)PIDList.Aileron] = new PID_Controller(defaultAileronGains);
            controllers[(int)PIDList.Rudder] = new PID_Controller(defaultRudderGains);
            controllers[(int)PIDList.Altitude] = new PID_Controller(defaultAltitudeGains);
            controllers[(int)PIDList.VertSpeed] = new PID_Controller(defaultVSpeedGains);
            controllers[(int)PIDList.Elevator] = new PID_Controller(defaultElevatorGains);
            controllers[(int)PIDList.Throttle] = new PID_Controller(defaultThrottleGains);

            // Set up a default preset that can be easily returned to
            if (PresetManager.Instance.craftPresetList.ContainsKey("default"))
            {
                if (PresetManager.Instance.craftPresetList["default"].AsstPreset == null)
                    PresetManager.Instance.craftPresetList["default"].AsstPreset = new AsstPreset(controllers, "default");
            }
            else
                PresetManager.Instance.craftPresetList.Add("default", new CraftPreset("default", new AsstPreset(controllers, "default"), null, null, true));

            PresetManager.saveDefaults();

            init = true;
        }

        private void vesselSwitch(Vessel v)
        {
            FlightData.thisVessel.OnPreAutopilotUpdate -= new FlightInputCallback(preAutoPilotEvent);
            FlightData.thisVessel.OnPostAutopilotUpdate -= new FlightInputCallback(vesselController);
            FlightData.thisVessel = v;
            FlightData.thisVessel.OnPostAutopilotUpdate += new FlightInputCallback(vesselController);
            FlightData.thisVessel.OnPreAutopilotUpdate += new FlightInputCallback(preAutoPilotEvent);

            PresetManager.loadCraftAsstPreset();
        }

        private void warpHandler()
        {
            if (TimeWarp.CurrentRateIndex == 0 && TimeWarp.CurrentRate != 1 && TimeWarp.WarpMode == TimeWarp.Modes.HIGH)
                bHdgWasActive = bVertWasActive = bThrottleWasActive = false;
        }

        public void OnDestroy()
        {
            RenderingManager.RemoveFromPostDrawQueue(5, drawGUI);
            GameEvents.onVesselChange.Remove(vesselSwitch);
            GameEvents.onTimeWarpRateChanged.Remove(warpHandler);
            PresetManager.saveToFile();
            bHdgActive = false;
            bVertActive = false;
        }

        public void Update()
        {
            keyPressChanges();

            if (IsPaused())
                return;

            if (bHdgActive != bHdgWasActive)
                hdgToggle();

            if (bVertActive != bVertWasActive)
                vertToggle();

            if (bAltitudeHold != bWasAltitudeHold)
                altToggle();

            if (bWingLeveller != bWasWingLeveller)
                wingToggle();

            if (bThrottleActive != bThrottleWasActive)
                throttleToggle();

            if (bHdgActive)
            {
                if (!FlightData.thisVessel.checkLanded())
                    PIDList.HdgBank.GetAsst().SetPoint = calculateTargetHeading(axisLock);
                else
                    PIDList.HdgBank.GetAsst().SetPoint = FlightData.heading;

                if (!headingEdit)
                    targetHeading = PIDList.HdgBank.GetAsst().SetPoint.ToString("0.00");
            }
        }

        public void drawGUI()
        {
            if (!AppLauncherFlight.bDisplayAssistant)
                return;

            GUI.skin = GeneralUI.UISkin;

            // Window resizing (scroll views dont work nicely with GUILayout)
            // Have to put the width changes before the draw so the close button is correctly placed
            float width;
            if (showPIDLimits && controllers.Any(c => c.bShow)) // use two column view if show limits option and a controller is open
                width = 370;
            else
                width = 240;

            if (bShowHdg)
            {
                hdgScrollHeight = 0; // no controllers visible when in wing lvl mode unless ctrl surf's are there
                if (!bWingLeveller)
                    hdgScrollHeight += 55; // hdg & yaw headers
                if ((PIDList.HdgBank.GetAsst().bShow || PIDList.BankToYaw.GetAsst().bShow) && !bWingLeveller)
                    hdgScrollHeight += 150; // open controller
                else if (showControlSurfaces)
                {
                    hdgScrollHeight += 50; // aileron and rudder headers
                    if (PIDList.Aileron.GetAsst().bShow || PIDList.Rudder.GetAsst().bShow)
                        hdgScrollHeight += 100; // open controller
                }
            }
            if (bShowVert)
            {
                vertScrollHeight = 38; // Vspeed header
                if (bAltitudeHold)
                    vertScrollHeight += 27; // altitude header
                if ((PIDList.Altitude.GetAsst().bShow && bAltitudeHold) || PIDList.VertSpeed.GetAsst().bShow)
                    vertScrollHeight += 150; // open  controller
                else if (showControlSurfaces)
                {
                    vertScrollHeight += 27; // elevator header
                    if (PIDList.Elevator.GetAsst().bShow)
                        vertScrollHeight += 123; // open controller
                }
            }
            // main window
            GUI.backgroundColor = GeneralUI.stockBackgroundGUIColor;
            window = GUILayout.Window(34244, window, displayWindow, "Pilot Assistant", GUILayout.Height(0), GUILayout.Width(width));
            // tooltip window. Label skin is transparent so it's only drawing what's inside it
            if (tooltip != "" && showTooltips)
                GUILayout.Window(34246, new Rect(window.x + window.width, Screen.height - Input.mousePosition.y, 0, 0), tooltipWindow, "", GeneralUI.UISkin.label, GUILayout.Height(0), GUILayout.Width(300));

            if (showPresets)
            {
                // move the preset window to sit to the right of the main window, with the tops level
                presetWindow.x = window.x + window.width;
                presetWindow.y = window.y;

                GUILayout.Window(34245, presetWindow, displayPresetWindow, "Pilot Assistant Presets", GUILayout.Width(200), GUILayout.Height(0));
            }
        }

        private void preAutoPilotEvent(FlightCtrlState state)
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;
            if (FlightData.thisVessel == null)
                return;

            FlightData.updateAttitude();
        }

        private void vesselController(FlightCtrlState state)
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;
            if (FlightData.thisVessel == null)
                return;

            if (IsPaused() || FlightData.thisVessel.srfSpeed < 1)
                return;            

            // Heading Control
            if (bHdgActive)
            {
                if (!bWingLeveller)
                {
                    // calculate the bank angle response based on the current heading
                    double hdgBankResponse = PIDList.HdgBank.GetAsst().ResponseD(CurrentAngleTargetRel(FlightData.progradeHeading, PIDList.HdgBank.GetAsst().SetPoint));
                    // aileron setpoint updated, bank angle also used for yaw calculations (don't go direct to rudder because we want yaw stabilisation *or* turn assistance)
                    PIDList.BankToYaw.GetAsst().SetPoint = PIDList.Aileron.GetAsst().SetPoint = hdgBankResponse;
                    PIDList.Rudder.GetAsst().SetPoint = -PIDList.BankToYaw.GetAsst().ResponseD(FlightData.yaw);
                }
                else
                {
                    bWasWingLeveller = true;
                    PIDList.Aileron.GetAsst().SetPoint = 0;
                    PIDList.Rudder.GetAsst().SetPoint = 0;
                }

                float rollInput = 0;
                if (GameSettings.ROLL_LEFT.GetKey())
                    rollInput = -1;
                else if (GameSettings.ROLL_RIGHT.GetKey())
                    rollInput = 1;
                else if (!GameSettings.AXIS_ROLL.IsNeutral())
                    rollInput = GameSettings.AXIS_ROLL.GetAxis();
                if (FlightInputHandler.fetch.precisionMode)
                    rollInput *= 0.33f;

                if (!FlightData.thisVessel.checkLanded())
                {
                    state.roll = (PIDList.Aileron.GetAsst().ResponseF(FlightData.roll) + rollInput).Clamp(-1, 1);
                    state.yaw = PIDList.Rudder.GetAsst().ResponseF(FlightData.yaw).Clamp(-1, 1);
                }
            }

            if (bVertActive)
            {
                // Set requested vertical speed
                if (bAltitudeHold)
                    PIDList.VertSpeed.GetAsst().SetPoint = -PIDList.Altitude.GetAsst().ResponseD(FlightData.thisVessel.altitude);

                PIDList.Elevator.GetAsst().SetPoint = -PIDList.VertSpeed.GetAsst().ResponseD(FlightData.vertSpeed);
                state.pitch = -PIDList.Elevator.GetAsst().ResponseF(FlightData.AoA).Clamp(-1, 1);
            }

            if (bThrottleActive && PIDList.Throttle.GetAsst().SetPoint != 0)
                state.mainThrottle = (-PIDList.Throttle.GetAsst().ResponseF(FlightData.thisVessel.srfSpeed)).Clamp(0, 1);
            else if (bThrottleActive && PIDList.Throttle.GetAsst().SetPoint == 0)
                state.mainThrottle = 0;
        }

        /// <summary>
        /// calculates the angle to feed corrected for 0/360 crossings
        /// eg. if the target is 350 and the current is 10, it will return 370 giving a diff of -20 degrees
        /// else you get +ve 340 and the turn is in the wrong direction
        /// </summary>
        double CurrentAngleTargetRel(double current, double target)
        {
            if (target - current < -180)
                return current - 360;
            else if (target - current > 180)
                return current + 360;
            else
                return current;
        }

        public static bool IsPaused()
        {
            return Instance.bPause;
        }

        private void hdgToggle()
        {
            bHdgWasActive = bHdgActive;

            PIDList.HdgBank.GetAsst().skipDerivative = true;
            PIDList.BankToYaw.GetAsst().skipDerivative = true;
            PIDList.Aileron.GetAsst().skipDerivative = true;
            PIDList.Rudder.GetAsst().skipDerivative = true;

            if (bHdgActive)
            {
                PIDList.HdgBank.GetAsst().SetPoint = FlightData.heading - (FlightData.roll / PIDList.HdgBank.GetAsst().PGain).Clamp(PIDList.HdgBank.GetAsst().OutMin, PIDList.HdgBank.GetAsst().OutMax);
                stop = false;
                StartCoroutine(shiftHeadingTarget(FlightData.heading));

                bPause = false;
                headingEdit = false;
            }
            else
            {
                stop = true;
                PIDList.HdgBank.GetAsst().Clear();
                PIDList.BankToYaw.GetAsst().Clear();
                PIDList.Aileron.GetAsst().Clear();
                PIDList.Rudder.GetAsst().Clear();
            }
        }

        private void vertToggle()
        {
            bVertWasActive = bVertActive;
            PIDList.VertSpeed.GetAsst().skipDerivative = true;
            PIDList.Elevator.GetAsst().skipDerivative = true;

            if (bVertActive)
            {
                PIDList.VertSpeed.GetAsst().Preset(-FlightData.AoA);
                PIDList.Elevator.GetAsst().Preset(-SurfSAS.Instance.pitchSet);

                if (bAltitudeHold)
                {
                    PIDList.Altitude.GetAsst().Preset(-FlightData.vertSpeed);
                    PIDList.Altitude.GetAsst().skipDerivative = true;

                    PIDList.Altitude.GetAsst().SetPoint = FlightData.thisVessel.altitude + FlightData.vertSpeed / PIDList.Altitude.GetAsst().PGain;
                    PIDList.Altitude.GetAsst().BumplessSetPoint = FlightData.thisVessel.altitude;
                    targetVert = PIDList.Altitude.GetAsst().SetPoint.ToString("0.00");
                }
                else
                {
                    PIDList.VertSpeed.GetAsst().SetPoint = FlightData.vertSpeed + FlightData.AoA / PIDList.VertSpeed.GetAsst().PGain;
                    PIDList.VertSpeed.GetAsst().BumplessSetPoint = FlightData.vertSpeed;
                    targetVert = PIDList.VertSpeed.GetAsst().SetPoint.ToString("0.00");
                }
                bPause = false;
            }
            else
            {
                PIDList.Altitude.GetAsst().Clear();
                PIDList.VertSpeed.GetAsst().Clear();
                PIDList.Elevator.GetAsst().Clear();
            }
        }

        private void altToggle()
        {
            bWasAltitudeHold = bAltitudeHold;
            if (bAltitudeHold)
            {
                PIDList.Altitude.GetAsst().SetPoint = FlightData.thisVessel.altitude + FlightData.vertSpeed / PIDList.Altitude.GetAsst().PGain;
                PIDList.Altitude.GetAsst().BumplessSetPoint = FlightData.thisVessel.altitude;
                targetVert = PIDList.Altitude.GetAsst().SetPoint.ToString("0.00");
            }
            else
            {
                PIDList.VertSpeed.GetAsst().SetPoint = FlightData.vertSpeed + FlightData.AoA / PIDList.VertSpeed.GetAsst().PGain;
                PIDList.VertSpeed.GetAsst().BumplessSetPoint = FlightData.vertSpeed;
                targetVert = PIDList.VertSpeed.GetAsst().SetPoint.ToString("0.00");
            }
        }

        private void wingToggle()
        {
            bWasWingLeveller = bWingLeveller;
            if (!bWingLeveller)
            {
                setAxisLock(FlightData.heading);
                targetHeading = PIDList.HdgBank.GetAsst().SetPoint.ToString("0.00");
                headingEdit = false;
            }
        }

        private void throttleToggle()
        {
            bThrottleWasActive = bThrottleActive;
            if (bThrottleActive)
            {
                PIDList.Throttle.GetAsst().SetPoint = FlightData.thisVessel.srfSpeed;
                targetSpeed = PIDList.Throttle.GetAsst().SetPoint.ToString("0.00");
            }
            else
                PIDList.Throttle.GetAsst().Clear();
        }

        private void keyPressChanges()
        {
            bool mod = GameSettings.MODIFIER_KEY.GetKey();

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                bHdgWasActive = false; // reset locks on unpausing
                bVertWasActive = false;
                bThrottleWasActive = false;

                bPause = !bPause;
                Messaging.statusMessage(bPause ? 0 : 1);
            }
            if (Utils.isFlightControlLocked())
                return;
            
            // update targets
            if (GameSettings.SAS_TOGGLE.GetKeyDown())
            {
                bHdgWasActive = false;
                bVertWasActive = false;
            }

            if (mod && Input.GetKeyDown(KeyCode.X))
            {
                PIDList.VertSpeed.GetAsst().SetPoint = 0;
                PIDList.Throttle.GetAsst().SetPoint = FlightData.thisVessel.srfSpeed;
                bAltitudeHold = false;
                bWasAltitudeHold = false;
                bWingLeveller = true;
                targetVert = "0.00";
                targetSpeed = FlightData.thisVessel.srfSpeed.ToString("0.00");
                Messaging.statusMessage(4);
            }

            if (Input.GetKeyDown(KeyCode.Keypad9) && GameSettings.MODIFIER_KEY.GetKey())
                bHdgActive = !bHdgActive;
            if (Input.GetKeyDown(KeyCode.Keypad6) && GameSettings.MODIFIER_KEY.GetKey())
                bVertActive = !bVertActive;
            if (Input.GetKeyDown(KeyCode.Keypad3) && GameSettings.MODIFIER_KEY.GetKey())
                bThrottleActive = !bThrottleActive;

            if (!IsPaused())
            {
                double scale = mod ? 10 : 1;
                bool bFineControl = FlightInputHandler.fetch.precisionMode;

                if (bVertActive && (GameSettings.PITCH_DOWN.GetKey() || GameSettings.PITCH_UP.GetKey() || (!GameSettings.AXIS_PITCH.IsNeutral() && Math.Abs(GameSettings.AXIS_PITCH.GetAxis()) > 0.000001f)))
                {
                    double vert = double.Parse(targetVert);
                    if (bAltitudeHold)
                        vert /= 10;

                    if (GameSettings.PITCH_DOWN.GetKey())
                        vert -= bFineControl ? 0.04 / scale : 0.4 * scale;
                    else if (GameSettings.PITCH_UP.GetKey())
                        vert += bFineControl ? 0.04 / scale : 0.4 * scale;
                    else if (!GameSettings.AXIS_PITCH.IsNeutral())
                        vert += (bFineControl ? 0.04 / scale : 0.4 * scale) * GameSettings.AXIS_PITCH.GetAxis();

                    if (bAltitudeHold)
                    {
                        vert = Math.Max(vert * 10, 0);
                        PIDList.Altitude.GetAsst().SetPoint = vert;
                        targetVert = vert.ToString("0.00");
                    }
                    else
                    {
                        PIDList.VertSpeed.GetAsst().SetPoint = vert;
                        targetVert = vert.ToString("0.00");
                    }
                }

                if (bThrottleActive && ((GameSettings.THROTTLE_UP.GetKey() || GameSettings.THROTTLE_DOWN.GetKey()) || (GameSettings.THROTTLE_CUTOFF.GetKeyDown() && !GameSettings.MODIFIER_KEY.GetKey()) || GameSettings.THROTTLE_FULL.GetKeyDown()))
                {
                    double speed = double.Parse(targetSpeed);

                    if (GameSettings.THROTTLE_UP.GetKey())
                        speed += bFineControl ? 0.1 / scale : 1 * scale;
                    else if (GameSettings.THROTTLE_DOWN.GetKey())
                        speed -= bFineControl ? 0.1 / scale : 1 * scale;

                    if (GameSettings.THROTTLE_CUTOFF.GetKeyDown() && !GameSettings.MODIFIER_KEY.GetKey())
                        speed = 0;
                    if (GameSettings.THROTTLE_FULL.GetKeyDown())
                        speed = 2400;

                    PIDList.Throttle.GetAsst().SetPoint = speed;

                    targetSpeed = Math.Max(speed, 0).ToString("0.00");
                }
            }
        }

        public static bool SASMonitor()
        {
            return (FlightData.thisVessel.ActionGroups[KSPActionGroup.SAS] || SurfSAS.ActivityCheck());
        }

        /// <summary>
        /// calculate current heading from target vector
        /// </summary>
        private double calculateTargetHeading(Vector3 axisLock)
        {
            Vector3 fwd = Vector3.Cross(FlightData.planetUp, axisLock);
            double heading = -1 * Vector3.Angle(fwd, FlightData.planetNorth) * Math.Sign(Vector3.Dot(fwd, FlightData.planetEast));
            if (heading < 0)
                heading += 360;
            return heading;
        }

        /// <summary>
        /// set the target heading vector to a given heading
        /// </summary>
        private void setAxisLock(double heading)
        {
            double diff = heading - FlightData.heading;
            axisLock = Quaternion.AngleAxis((float)(diff - 90), (Vector3)FlightData.planetUp) * FlightData.surfVesForward;
        }

        /// <summary>
        /// Get the direction vector for a given heading
        /// </summary>
        private Vector3 vecHeading(double heading)
        {
            double angleDiff = heading - FlightData.heading;
            return Quaternion.AngleAxis((float)(angleDiff - 90), (Vector3)FlightData.planetUp) * FlightData.surfVesForward;
        }

        Vector3 currentTarget = Vector3.zero; // this is the vec the control is aimed at
        Vector3 newTarget = Vector3.zero; // this is the vec we are moving to
        double increment = 0; // this is the angle to shift per second
        bool running = false;
        bool stop = false;
        IEnumerator shiftHeadingTarget(double newHdg)
        {
            newTarget = vecHeading(newHdg);
            currentTarget = vecHeading(PIDList.HdgBank.GetAsst().BumplessSetPoint);
            increment = 0;

            if (running)
                yield break;
            running = true;

            while (!stop && Math.Abs(Vector3.Angle(currentTarget, newTarget)) > 0.01)
            {
                double finalTarget = calculateTargetHeading(newTarget);
                double target = calculateTargetHeading(currentTarget);
                increment += PIDList.HdgBank.GetAsst().Easing * TimeWarp.fixedDeltaTime * 0.01;

                double remainder = finalTarget - CurrentAngleTargetRel(target, finalTarget);
                if (remainder < 0)
                    target += Math.Max(-1 * increment, remainder);
                else
                    target += Math.Min(increment, remainder);

                setAxisLock(target);
                currentTarget = vecHeading(target);
                yield return new WaitForFixedUpdate();
            }
            if (!stop)
                axisLock = newTarget;
            running = false;
        }

        public double commitDelay = 0;
        double headingChangeToCommit; // The amount of heading change to commit when the timer expires
        double headingTimeToCommit; // update heading target when <= 0
        IEnumerator headingKeyboardResponse()
        {
            while (HighLogic.LoadedSceneIsFlight)
            {
                yield return null;
                if (!IsPaused() && bHdgActive && !FlightData.thisVessel.checkLanded())
                {
                    double scale = GameSettings.MODIFIER_KEY.GetKey() ? 10 : 1;
                    bool bFineControl = FlightInputHandler.fetch.precisionMode;
                    if (bHdgActive && (GameSettings.YAW_LEFT.GetKey() || GameSettings.YAW_RIGHT.GetKey() || (!GameSettings.AXIS_YAW.IsNeutral() && Math.Abs(GameSettings.AXIS_YAW.GetAxis()) > 0.000001f)))
                    {
                        if (GameSettings.YAW_LEFT.GetKey())
                            headingChangeToCommit -= bFineControl ? 0.04 / scale : 0.4 * scale;
                        else if (GameSettings.YAW_RIGHT.GetKey())
                            headingChangeToCommit += bFineControl ? 0.04 / scale : 0.4 * scale;
                        else if (!GameSettings.AXIS_YAW.IsNeutral())
                            headingChangeToCommit += (bFineControl ? 0.04 / scale : 0.4 * scale) * GameSettings.AXIS_YAW.GetAxis();

                        if (headingChangeToCommit < -180)
                            headingChangeToCommit += 360;
                        else if (headingChangeToCommit > 180)
                            headingChangeToCommit -= 360;

                        headingTimeToCommit = commitDelay;
                    }

                    if (headingTimeToCommit <= 0 && headingChangeToCommit != 0)
                    {
                        if (running)
                        {
                            newTarget = vecHeading(calculateTargetHeading(newTarget) + headingChangeToCommit);
                        }
                        else
                        {
                            stop = false;
                            setAxisLock(FlightData.heading + FlightData.roll / PIDList.HdgBank.GetAsst().PGain);
                            StartCoroutine(shiftHeadingTarget(calculateTargetHeading(newTarget) + headingChangeToCommit));
                            headingEdit = false;
                        }

                        headingChangeToCommit = 0;
                    }
                    else if (headingTimeToCommit > 0)
                        headingTimeToCommit -= TimeWarp.deltaTime;
                }
                else
                    headingChangeToCommit = 0;
            }
        }


        #region GUI
        private void displayWindow(int id)
        {
            if (GUI.Button(new Rect(window.width - 16, 2, 14, 14), ""))
                AppLauncherFlight.bDisplayAssistant = false;

            if (IsPaused())
                GUILayout.Box("CONTROL PAUSED", GeneralUI.UISkin.customStyles[(int)myStyles.labelAlert]);

            #region Hdg GUI

            GUILayout.BeginHorizontal();

            GUI.backgroundColor = GeneralUI.HeaderButtonBackground;
            bShowHdg = GUILayout.Toggle(bShowHdg, bShowHdg ? "-" : "+", GeneralUI.UISkin.customStyles[(int)myStyles.btnToggle], GUILayout.Width(20));

            if (bHdgActive)
                GUI.backgroundColor = GeneralUI.ActiveBackground;
            else
                GUI.backgroundColor = GeneralUI.InActiveBackground;
            if (GUILayout.Button("Roll and Yaw Control", GUILayout.Width(186)))
            {
                bHdgActive = !bHdgActive;
                bPause = false;
            }

            // reset colour
            GUI.backgroundColor = GeneralUI.stockBackgroundGUIColor;
            GUILayout.EndHorizontal();

            if (bShowHdg)
            {
                bWingLeveller = GUILayout.Toggle(bWingLeveller, bWingLeveller ? "Mode: Wing Leveller" : "Mode: Hdg Control", GUILayout.Width(200));
                if (!bWingLeveller)
                {
                    GUILayout.BeginHorizontal();
                    double newHdg;
                    bool valid = double.TryParse(targetHeading, out newHdg);
                    if (GUILayout.Button("Target Hdg: ", GUILayout.Width(90)))
                    {
                        headingEdit = false;
                        if (valid && newHdg >= 0 && newHdg <= 360)
                        {
                            PIDList.HdgBank.GetAsst().SetPoint = FlightData.heading - (FlightData.roll / PIDList.HdgBank.GetAsst().PGain).Clamp(PIDList.HdgBank.GetAsst().OutMin, PIDList.HdgBank.GetAsst().OutMax);
                            stop = false;
                            StartCoroutine(shiftHeadingTarget(newHdg));
                            bHdgActive = bHdgWasActive = true; // skip toggle check to avoid being overwritten

                            GUI.FocusControl("Target Hdg: ");
                            GUI.UnfocusWindow();
                        }
                    }

                    double displayTargetDelta; // active setpoint or absolute value to change (yaw L/R input)
                    string displayTarget; // target setpoint or setpoint to commit as target setpoint

                    if (headingChangeToCommit != 0)
                        displayTargetDelta = headingChangeToCommit;
                    else
                    {
                        if (!running)
                            displayTargetDelta = PIDList.HdgBank.GetAsst().SetPoint - FlightData.heading;
                        else
                            displayTargetDelta = calculateTargetHeading(newTarget) - FlightData.heading;
                        if (displayTargetDelta > 180)
                            displayTargetDelta -= 360;
                        else if (displayTargetDelta < -180)
                            displayTargetDelta += 360;
                    }

                    if (headingEdit)
                        displayTarget = targetHeading;
                    else if (headingChangeToCommit == 0 || FlightData.thisVessel.checkLanded())
                        displayTarget = calculateTargetHeading(newTarget).ToString("0.00");
                    else
                    {
                        double val = calculateTargetHeading(newTarget) + headingChangeToCommit;
                        if (val > 360)
                            val -= 360;
                        else if (val < 0)
                            val += 360;
                        displayTarget = val.ToString("0.00");
                    }

                    targetHeading = GUILayout.TextField(displayTarget, GUILayout.Width(47));
                    if (targetHeading != displayTarget)
                        headingEdit = true;
                    GUILayout.Label(displayTargetDelta.ToString("0.00"), GeneralUI.UISkin.customStyles[(int)myStyles.greenTextBox], GUILayout.Width(55));
                    GUILayout.EndHorizontal();
                }

                scrollbarHdg = GUILayout.BeginScrollView(scrollbarHdg, GUIStyle.none, GeneralUI.UISkin.verticalScrollbar, GUILayout.Height(hdgScrollHeight));
                if (!bWingLeveller)
                {
                    drawPIDvalues(PIDList.HdgBank, "Heading", "\u00B0", FlightData.heading, 2, "Bank", "\u00B0");
                    drawPIDvalues(PIDList.BankToYaw, "Yaw", "\u00B0", FlightData.yaw, 2, "Yaw", "\u00B0", true, false);
                }
                if (showControlSurfaces)
                {
                    drawPIDvalues(PIDList.Aileron, "Bank", "\u00B0", FlightData.roll, 3, "Deflection", "\u00B0");
                    drawPIDvalues(PIDList.Rudder, "Yaw", "\u00B0", FlightData.yaw, 3, "Deflection", "\u00B0");
                }
                GUILayout.EndScrollView();

                PIDList.Aileron.GetAsst().OutMin = Math.Min(Math.Max(PIDList.Aileron.GetAsst().OutMin, -1), 1);
                PIDList.Aileron.GetAsst().OutMax = Math.Min(Math.Max(PIDList.Aileron.GetAsst().OutMax, -1), 1);

                PIDList.Rudder.GetAsst().OutMin = Math.Min(Math.Max(PIDList.Rudder.GetAsst().OutMin, -1), 1);
                PIDList.Rudder.GetAsst().OutMax = Math.Min(Math.Max(PIDList.Rudder.GetAsst().OutMax, -1), 1);
            }
            #endregion

            #region Pitch GUI

            GUILayout.BeginHorizontal();

            GUI.backgroundColor = GeneralUI.HeaderButtonBackground;
            bShowVert = GUILayout.Toggle(bShowVert, bShowVert ? "-" : "+", GeneralUI.UISkin.customStyles[(int)myStyles.btnToggle], GUILayout.Width(20));

            if (bVertActive)
                GUI.backgroundColor = GeneralUI.ActiveBackground;
            else
                GUI.backgroundColor = GeneralUI.InActiveBackground;
            if (GUILayout.Button("Vertical Control", GUILayout.Width(186)))
            {
                bVertActive = !bVertActive;
                bPause = false;
            }
           
            // reset colour
            GUI.backgroundColor = GeneralUI.stockBackgroundGUIColor;
            GUILayout.EndHorizontal();

            if (bShowVert)
            {
                bAltitudeHold = GUILayout.Toggle(bAltitudeHold, bAltitudeHold ? "Mode: Altitude" : "Mode: Vertical Speed", GUILayout.Width(200));

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(bAltitudeHold ? "Target Altitude:" : "Target Speed:", GUILayout.Width(98)))
                {
                    ScreenMessages.PostScreenMessage("Target " + (PilotAssistant.Instance.bAltitudeHold ? "Altitude" : "Vertical Speed") + " updated");

                    double newVal;
                    double.TryParse(targetVert, out newVal);
                    if (bAltitudeHold)
                    {
                        PIDList.Altitude.GetAsst().SetPoint = FlightData.thisVessel.altitude + FlightData.vertSpeed / PIDList.Altitude.GetAsst().PGain;
                        PIDList.Altitude.GetAsst().BumplessSetPoint = newVal;
                    }
                    else
                    {
                        PIDList.VertSpeed.GetAsst().SetPoint = FlightData.thisVessel.verticalSpeed + FlightData.AoA / PIDList.VertSpeed.GetAsst().PGain;
                        PIDList.VertSpeed.GetAsst().BumplessSetPoint = newVal;
                    }

                    bVertActive = bVertWasActive = true; // skip the toggle check so value isn't overwritten

                    GUI.FocusControl("Target Hdg: ");
                    GUI.UnfocusWindow();
                }
                targetVert = GUILayout.TextField(targetVert, GUILayout.Width(98));
                GUILayout.EndHorizontal();

                scrollbarVert = GUILayout.BeginScrollView(scrollbarVert, GUIStyle.none, GeneralUI.UISkin.verticalScrollbar, GUILayout.Height(vertScrollHeight));

                if (bAltitudeHold)
                    drawPIDvalues(PIDList.Altitude, "Altitude", "m", FlightData.thisVessel.altitude, 2, "Speed ", "m/s", true);
                drawPIDvalues(PIDList.VertSpeed, "Vertical Speed", "m/s", FlightData.vertSpeed, 2, "AoA", "\u00B0", true);

                if (showControlSurfaces)
                    drawPIDvalues(PIDList.Elevator, "Angle of Attack", "\u00B0", FlightData.AoA, 3, "Deflection", "\u00B0", true);

                PIDList.Elevator.GetAsst().OutMin = Math.Min(Math.Max(PIDList.Elevator.GetAsst().OutMin, -1), 1);
                PIDList.Elevator.GetAsst().OutMax = Math.Min(Math.Max(PIDList.Elevator.GetAsst().OutMax, -1), 1);

                GUILayout.EndScrollView();
            }
            #endregion

            #region Throttle GUI

            GUILayout.BeginHorizontal();
            // button background
            GUI.backgroundColor = GeneralUI.HeaderButtonBackground;
            bShowThrottle = GUILayout.Toggle(bShowThrottle, bShowThrottle ? "-" : "+", GeneralUI.UISkin.customStyles[(int)myStyles.btnToggle], GUILayout.Width(20));
            if (bThrottleActive)
                GUI.backgroundColor = GeneralUI.ActiveBackground;
            else
                GUI.backgroundColor = GeneralUI.InActiveBackground;
            if (GUILayout.Button("Throttle Control", GUILayout.Width(186)))
            {
                bThrottleActive = !bThrottleActive;
                if (!bThrottleActive)
                    bPause = false;
            }
            // reset colour
            GUI.backgroundColor = GeneralUI.stockBackgroundGUIColor;
            GUILayout.EndHorizontal();

            if (bShowThrottle)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Target Speed:", GUILayout.Width(118)))
                {
                    ScreenMessages.PostScreenMessage("Target Speed updated");

                    double newVal;
                    double.TryParse(targetSpeed, out newVal);
                    PIDList.Throttle.GetAsst().SetPoint = FlightData.thisVessel.srfSpeed;
                    PIDList.Throttle.GetAsst().BumplessSetPoint = newVal;

                    bThrottleActive = bThrottleWasActive = true; // skip the toggle check so value isn't overwritten

                    GUI.FocusControl("Target Hdg: ");
                    GUI.UnfocusWindow();
                }
                targetSpeed = GUILayout.TextField(targetSpeed, GUILayout.Width(78));
                GUILayout.EndHorizontal();

                drawPIDvalues(PIDList.Throttle, "Speed", "m/s", FlightData.thisVessel.srfSpeed, 2, "Throttle", "", true);
                // can't have people bugging things out now can we...
                PIDList.Throttle.GetAsst().OutMin = PIDList.Throttle.GetAsst().OutMin.Clamp(-1, 0);
                PIDList.Throttle.GetAsst().OutMax = PIDList.Throttle.GetAsst().OutMax.Clamp(-1, 0);
            }

            #endregion

            GUI.backgroundColor = GeneralUI.HeaderButtonBackground;
            if (GUILayout.Button("Options", GUILayout.Width(205)))
            {
                bShowSettings = !bShowSettings;
            }
            GUI.backgroundColor = GeneralUI.stockBackgroundGUIColor;
            if (bShowSettings)
            {
                showPresets = GUILayout.Toggle(showPresets, showPresets ? "Hide Presets" : "Show Presets", GUILayout.Width(200));
                showPIDLimits = GUILayout.Toggle(showPIDLimits, showPIDLimits ? "Hide PID Limits" : "Show PID Limits", GUILayout.Width(200));
                showControlSurfaces = GUILayout.Toggle(showControlSurfaces, showControlSurfaces ? "Hide Control Surfaces" : "Show Control Surfaces", GUILayout.Width(200));
                doublesided = GUILayout.Toggle(doublesided, "Separate Min and Max limits", GUILayout.Width(200));
                showTooltips = GUILayout.Toggle(showTooltips, "Show Tooltips", GUILayout.Width(200));

                GUILayout.BeginHorizontal();
                GUILayout.Label("Input delay", GUILayout.Width(98));
                string text = GUILayout.TextField(commitDelay.ToString("0.0"), GUILayout.Width(98));
                try
                {
                    commitDelay = double.Parse(text);
                }
                catch { } // if the conversion fails it just reverts to the last good value. No need for further action
                GUILayout.EndHorizontal();
            }

            GUI.DragWindow();
            if (Event.current.type == EventType.Repaint)
                tooltip = GUI.tooltip;
        }

        
        string OutMaxTooltip = "The absolute maximum value the controller can output";
        string OutMinTooltip = "The absolute minimum value the controller can output";

        string tooltip = "";
        private void tooltipWindow(int id)
        {
            GUILayout.Label(tooltip, GeneralUI.UISkin.textArea);
        }

        private void drawPIDvalues(PIDList controllerid, string inputName, string inputUnits, double inputValue, int displayPrecision, string outputName, string outputUnits, bool invertOutput = false, bool showTarget = true)
        {
            PID_Controller controller = controllerid.GetAsst();
            controller.bShow = GUILayout.Toggle(controller.bShow, string.Format("{0}: {1}{2}", inputName, inputValue.ToString("N" + displayPrecision.ToString()), inputUnits), GeneralUI.UISkin.customStyles[(int)myStyles.btnToggle], GUILayout.Width(200));

            if (controller.bShow)
            {
                if (showTarget)
                    GUILayout.Label("Target: " + controller.SetPoint.ToString("N" + displayPrecision.ToString()) + inputUnits, GUILayout.Width(200));

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();

                controller.PGain = GeneralUI.labPlusNumBox(GeneralUI.KpLabel, controller.PGain.ToString("G3"), 45);
                controller.IGain = GeneralUI.labPlusNumBox(GeneralUI.KiLabel, controller.IGain.ToString("G3"), 45);
                controller.DGain = GeneralUI.labPlusNumBox(GeneralUI.KdLabel, controller.DGain.ToString("G3"), 45);
                controller.Scalar = GeneralUI.labPlusNumBox(GeneralUI.ScalarLabel, controller.Scalar.ToString("G3"), 45);

                if (showPIDLimits)
                {
                    GUILayout.EndVertical();
                    GUILayout.BeginVertical();

                    if (!invertOutput)
                    {
                        controller.OutMax = GeneralUI.labPlusNumBox(new GUIContent(string.Format("Max {0}{1}:", outputName, outputUnits), OutMaxTooltip), controller.OutMax.ToString("G3"));
                        if (doublesided)
                            controller.OutMin = GeneralUI.labPlusNumBox(new GUIContent(string.Format("Min {0}{1}:", outputName, outputUnits), OutMinTooltip), controller.OutMin.ToString("G3"));
                        else
                            controller.OutMin = -controller.OutMax;
                        if (doublesided)
                            controller.ClampLower = GeneralUI.labPlusNumBox(GeneralUI.IMinLabel, controller.ClampLower.ToString("G3"));
                        else
                            controller.ClampLower = -controller.ClampUpper;
                        controller.ClampUpper = GeneralUI.labPlusNumBox(GeneralUI.IMaxLabel, controller.ClampUpper.ToString("G3"));

                        controller.Easing = GeneralUI.labPlusNumBox(GeneralUI.EasingLabel, controller.Easing.ToString("G3"));
                    }
                    else
                    { // used when response * -1 is used to get the correct output
                        controller.OutMin = -1 * GeneralUI.labPlusNumBox(new GUIContent(string.Format("Max {0}{1}:", outputName, outputUnits), OutMaxTooltip), (-controller.OutMin).ToString("G3"));
                        if (doublesided)
                            controller.OutMax = -1 * GeneralUI.labPlusNumBox(new GUIContent(string.Format("Min {0}{1}:", outputName, outputUnits), OutMinTooltip), (-controller.OutMax).ToString("G3"));
                        else
                            controller.OutMax = -controller.OutMin;

                        if (doublesided)
                            controller.ClampUpper = -1 * GeneralUI.labPlusNumBox(GeneralUI.IMinLabel, (-controller.ClampUpper).ToString("G3"));
                        else
                            controller.ClampUpper = -controller.ClampLower;
                        controller.ClampLower = -1 * GeneralUI.labPlusNumBox(GeneralUI.IMaxLabel, (-controller.ClampLower).ToString("G3"));

                        controller.Easing = GeneralUI.labPlusNumBox(GeneralUI.EasingLabel, controller.Easing.ToString("G3"));
                    }
                }
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
        }

        private void displayPresetWindow(int id)
        {
            if (GUI.Button(new Rect(presetWindow.width - 16, 2, 14, 14), ""))
            {
                showPresets = false;
            }

            if (PresetManager.Instance.activePAPreset != null)
            {
                GUILayout.Label(string.Format("Active Preset: {0}", PresetManager.Instance.activePAPreset.name));
                if (PresetManager.Instance.activePAPreset.name != "default")
                {
                    if (GUILayout.Button("Update Preset"))
                        PresetManager.updatePAPreset(controllers);
                }
                GUILayout.Box("", GUILayout.Height(10), GUILayout.Width(180));
            }

            GUILayout.BeginHorizontal();
            newPresetName = GUILayout.TextField(newPresetName);
            if (GUILayout.Button("+", GUILayout.Width(25)))
                PresetManager.newPAPreset(ref newPresetName, controllers);
            GUILayout.EndHorizontal();

            GUILayout.Box("", GUILayout.Height(10), GUILayout.Width(180));

            if (GUILayout.Button("Reset to Defaults"))
                PresetManager.loadPAPreset(PresetManager.Instance.craftPresetList["default"].AsstPreset);

            GUILayout.Box("", GUILayout.Height(10), GUILayout.Width(180));

            foreach (AsstPreset p in PresetManager.Instance.PAPresetList)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(p.name))
                    PresetManager.loadPAPreset(p);
                else if (GUILayout.Button("x", GUILayout.Width(25)))
                    PresetManager.deletePAPreset(p);
                GUILayout.EndHorizontal();
            }
        }
        #endregion
    }
}