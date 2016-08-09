﻿/*****************************************************************************
 * The MIT License (MIT)
 * 
 * Copyright (c) 2016 MOARdV
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * 
 ****************************************************************************/
using MoonSharp.Interpreter;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace AvionicsSystems
{
    internal class MASIRealChute
    {
        internal static bool realChuteFound;
        internal static readonly Type rcAPI_t;

        private static readonly FieldInfo armed_t;
        private static readonly FieldInfo safeState_t;

        private static readonly MethodInfo armChute_t;
        private static readonly MethodInfo cutChute_t;
        private static readonly MethodInfo deployChute_t;
        private static readonly MethodInfo disarmChute_t;
        private static readonly MethodInfo getAnyDeployed_t;

        // From RealChute:
        private enum SafeState
        {
            SAFE,
            RISKY,
            DANGEROUS
        }

        internal Vessel vessel;
        internal MASVesselComputer vc;

        internal Func<bool>[] getAnyDeployed = new Func<bool>[0];
        internal Action[] armParachute = new Action[0];
        internal Action[] cutRealChute = new Action[0];
        internal Action[] deployRealChute = new Action[0];
        internal Action[] disarmParachute = new Action[0];

        private bool allSafe;
        private bool allDangerous;
        private bool anyArmed;
        private bool anyDeployed;

        [MoonSharpHidden]
        public MASIRealChute(Vessel vessel)
        {
            this.vessel = vessel;
            anyArmed = false;
        }

        ~MASIRealChute()
        {
            vessel = null;
            vc = null;
        }

        /// <summary>
        /// Cut all deployed parachutes (RealChute as well as stock).
        /// </summary>
        public void CutParachute()
        {
            for (int i = cutRealChute.Length - 1; i >= 0; --i)
            {
                cutRealChute[i]();
            }

            for (int i = vc.moduleParachute.Length - 1; i >= 0; --i)
            {
                if (vc.moduleParachute[i].deploymentState == ModuleParachute.deploymentStates.DEPLOYED || vc.moduleParachute[i].deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED)
                {
                    vc.moduleParachute[i].CutParachute();
                }
            }
        }

        /// <summary>
        /// Deploys any parachutes (RealChute as well as stock).
        /// </summary>
        public void DeployParachute()
        {
            for (int i = deployRealChute.Length - 1; i >= 0; --i)
            {
                deployRealChute[i]();
            }

            for (int i = vc.moduleParachute.Length - 1; i >= 0; --i)
            {
                if (vc.moduleParachute[i].deploymentState == ModuleParachute.deploymentStates.STOWED)
                {
                    vc.moduleParachute[i].Deploy();
                }
            }
        }

        /// <summary>
        /// Returns 1 if it is safe to deploy all parachutes, 0 if it is safe for
        /// some parachutes, or -1 if it is dangerous for all parachutes.  Returns
        /// 1 if there are no parachutes.
        /// </summary>
        /// <returns></returns>
        public double DeploymentSafe()
        {
            if (allSafe)
            {
                return 1.0;
            }
            else if (allDangerous)
            {
                return -1.0;
            }
            else
            {
                return 0.0;
            }
        }

        /// <summary>
        /// Returns 1 if at least one RealChute parachute is armed, 0
        /// otherwise.
        /// </summary>
        /// <returns></returns>
        public double GetParachuteArmed()
        {
            return (anyArmed) ? 1.0 : 0.0;
        }

        /// <summary>
        /// Returns 1 is at least one RealChute parachute is armed or deployed,
        /// or if any stock parachutes are deployed; 0 otherwise.
        /// </summary>
        /// <returns></returns>
        public double GetParachuteArmedOrDeployed()
        {
            return (anyArmed || anyDeployed) ? 1.0 : 0.0;
        }

        /// <summary>
        /// Returns 1 if at least one RealChute or stock parachute is deployed;
        /// 0 otherwise.
        /// </summary>
        /// <returns></returns>
        public double GetParachuteDeployed()
        {
            return (anyDeployed) ? 1.0 : 0.0;
        }

        /// <summary>
        /// Toggles the armed state of any RealChute parachutes.
        /// </summary>
        public void ToggleParachuteArmed()
        {
            if (anyArmed)
            {
                for (int i = disarmParachute.Length - 1; i >= 0; --i)
                {
                    disarmParachute[i]();
                }
            }
            else
            {
                for (int i = armParachute.Length - 1; i >= 0; --i)
                {
                    armParachute[i]();
                }
            }
        }

        /// <summary>
        /// Method called during FixedUpdate to update queryable variables that
        /// are used by multiple methods.
        /// </summary>
        [MoonSharpHidden]
        internal void Update()
        {
            int newLength = vc.moduleRealChute.Length;
            if (newLength != armParachute.Length)
            {
                // The module count has changed: we need to rebuild all of our delegates.
                getAnyDeployed = new Func<bool>[newLength];
                armParachute = new Action[newLength];
                cutRealChute = new Action[newLength];
                deployRealChute = new Action[newLength];
                disarmParachute = new Action[newLength];

                for (int i = 0; i < newLength; ++i)
                {
                    getAnyDeployed[i] = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), vc.moduleRealChute[i], getAnyDeployed_t);
                    armParachute[i] = (Action)Delegate.CreateDelegate(typeof(Action), vc.moduleRealChute[i], armChute_t);
                    cutRealChute[i] = (Action)Delegate.CreateDelegate(typeof(Action), vc.moduleRealChute[i], cutChute_t);
                    deployRealChute[i] = (Action)Delegate.CreateDelegate(typeof(Action), vc.moduleRealChute[i], deployChute_t);
                    disarmParachute[i] = (Action)Delegate.CreateDelegate(typeof(Action), vc.moduleRealChute[i], disarmChute_t);
                }
            }

            anyArmed = false;
            anyDeployed = false;
            allSafe = true;
            allDangerous = true;
            for (int i = 0; i < newLength; ++i)
            {
                if ((bool)armed_t.GetValue(vc.moduleRealChute[i]))
                {
                    anyArmed = true;
                    //break;
                }
                if (getAnyDeployed[i]())
                {
                    anyDeployed = true;
                }

                object safetyState_o = safeState_t.GetValue(vc.moduleRealChute[i]);
                int safetyState = (int)safetyState_o;
                if (safetyState != (int)SafeState.SAFE)
                {
                    allSafe = false;
                }
                if (safetyState != (int)SafeState.DANGEROUS)
                {
                    allDangerous = false;
                }
            }

            if (!anyDeployed || (allSafe && allDangerous))
            {
                for (int i = vc.moduleParachute.Length - 1; i >= 0; --i)
                {
                    if (vc.moduleParachute[i].deploymentState == ModuleParachute.deploymentStates.DEPLOYED ||
                        vc.moduleParachute[i].deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED)
                    {
                        anyDeployed = true;
                    }

                    if (vc.moduleParachute[i].deploymentSafeState != ModuleParachute.deploymentSafeStates.SAFE)
                    {
                        allSafe = false;
                    }
                    if (vc.moduleParachute[i].deploymentSafeState != ModuleParachute.deploymentSafeStates.RISKY)
                    {
                        allDangerous = false;
                    }
                }
            }
        }

        #region Reflection Configuration
        static MASIRealChute()
        {
            realChuteFound = false;
            rcAPI_t = Utility.GetExportedType("RealChute", "RealChute.RealChuteModule");
            if (rcAPI_t != null)
            {
                PropertyInfo rcAnyDeployed = rcAPI_t.GetProperty("AnyDeployed", BindingFlags.Instance | BindingFlags.Public);
                if (rcAnyDeployed == null)
                {
                    Utility.LogErrorMessage("rcAnyDeployed is null");
                    return;
                }
                getAnyDeployed_t = rcAnyDeployed.GetGetMethod();
                if (getAnyDeployed_t == null)
                {
                    Utility.LogErrorMessage("getAnyDeployed_t is null");
                    return;
                }

                armChute_t = rcAPI_t.GetMethod("GUIArm", BindingFlags.Instance | BindingFlags.Public);
                if (armChute_t == null)
                {
                    Utility.LogErrorMessage("armChute_t is null");
                    return;
                }

                disarmChute_t = rcAPI_t.GetMethod("GUIDisarm", BindingFlags.Instance | BindingFlags.Public);
                if (disarmChute_t == null)
                {
                    Utility.LogErrorMessage("disarmChute_t is null");
                    return;
                }

                deployChute_t = rcAPI_t.GetMethod("GUIDeploy", BindingFlags.Instance | BindingFlags.Public);
                if (deployChute_t == null)
                {
                    Utility.LogErrorMessage("deployChute_t is null");
                    return;
                }

                cutChute_t = rcAPI_t.GetMethod("GUICut", BindingFlags.Instance | BindingFlags.Public);
                if (cutChute_t == null)
                {
                    Utility.LogErrorMessage("cutChute_t is null");
                    return;
                }

                armed_t = rcAPI_t.GetField("armed", BindingFlags.Instance | BindingFlags.Public);
                if (armed_t == null)
                {
                    Utility.LogErrorMessage("armed_t is null");
                    return;
                }

                safeState_t = rcAPI_t.GetField("safeState", BindingFlags.Instance | BindingFlags.Public);
                if (safeState_t == null)
                {
                    Utility.LogErrorMessage("safeState_t is null");
                    return;
                }

                realChuteFound = true;
            }
        }
        #endregion
    }
}