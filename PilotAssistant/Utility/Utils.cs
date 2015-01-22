﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PilotAssistant.Utility
{
    using PID;

    public static class Utils
    {
        public static float Clamp(float val, float min, float max)
        {
            if (val < min)
                return min;
            else if (val > max)
                return max;
            else
                return val;
        }

        public static PID_Controller GetAsst(PIDList id)
        {
            return PilotAssistant.Instance.controllers[(int)id];
        }

        public static PID_Controller GetSAS(SASList id)
        {
            return SurfSAS.Instance.SASControllers[(int)id];
        }
    }
}
