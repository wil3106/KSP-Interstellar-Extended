﻿using FNPlugin.Constants;
using FNPlugin.Extensions;
using System;
using System.Linq;
using UnityEngine;

namespace FNPlugin.Refinery
{
    class AnthraquinoneProcessor : RefineryActivityBase, IRefineryActivity
    {
        double _fixedConsumptionRate;
        double _consumptionRate;

        double _hydrogen_density;
        double _oxygen_density;
        double _hydrogen_peroxide_density;

        string _oxygenResourceName;
        string _hydrogenResourceName;
        string _hydrogenPeroxideResourceName;

        double _maxCapacityOxygenMass;
        double _maxCapacityHydrogenMass;
        double _maxCapacityPeroxideMass;

        double _availableOxygenMass;
        double _availableHydrogenMass;
        double _spareRoomHydrogenPeroxideMass;

        double _hydrogen_consumption_rate;
        double _oxygen_consumption_rate;
        double _hydrogen_peroxide_production_rate;

        double _hydrogenMassByFraction = (1.0079 * 2)/ 34.01468;
        double _oxygenMassByFraction = 1 - ((1.0079 * 2) / 34.01468);

        public RefineryType RefineryType { get { return RefineryType.synthesize; } }

        public String ActivityName { get { return "Anthraquinone Process: H<size=7>2</size> + O<size=7>2</size> => H<size=7>2</size>O<size=7>2</size> (HTP) "; } }

        public bool HasActivityRequirements()
        {
            return _part.GetConnectedResources(InterstellarResourcesConfiguration.Instance.Hydrogen).Any(rs => rs.amount > 0) &
                _part.GetConnectedResources(InterstellarResourcesConfiguration.Instance.LqdOxygen).Any(rs => rs.amount > 0);
        }

        public double PowerRequirements { get { return PluginHelper.BaseAnthraquiononePowerConsumption; } }

        public String Status { get { return String.Copy(_status); } }

        public void Initialize(Part part)
        {
            _part = part;
            _vessel = part.vessel;

            _oxygenResourceName = InterstellarResourcesConfiguration.Instance.LqdOxygen;
            _hydrogenResourceName = InterstellarResourcesConfiguration.Instance.Hydrogen;
            _hydrogenPeroxideResourceName = InterstellarResourcesConfiguration.Instance.HydrogenPeroxide;

            _hydrogen_density = (double)(decimal)PartResourceLibrary.Instance.GetDefinition(InterstellarResourcesConfiguration.Instance.Hydrogen).density;
            _oxygen_density = (double)(decimal)PartResourceLibrary.Instance.GetDefinition(InterstellarResourcesConfiguration.Instance.LqdOxygen).density;
            _hydrogen_peroxide_density = (double)(decimal)PartResourceLibrary.Instance.GetDefinition(InterstellarResourcesConfiguration.Instance.HydrogenPeroxide).density;
        }

        public void UpdateFrame(double rateMultiplier, double powerFraction, double productionModidier, bool allowOverflow, double fixedDeltaTime, bool isStartup = false)
        {
            _effectiveMaxPower = PowerRequirements * productionModidier;

            // determine how much resource we have
            _current_power = _effectiveMaxPower * powerFraction;
            _current_rate = CurrentPower / PluginHelper.AnthraquinoneEnergyPerTon;

            var partsThatContainOxygen = _part.GetConnectedResources(_oxygenResourceName);
            var partsThatContainHydrogen = _part.GetConnectedResources(_hydrogenResourceName);
            var partsThatContainPeroxide = _part.GetConnectedResources(_hydrogenPeroxideResourceName);

            _maxCapacityOxygenMass = partsThatContainOxygen.Sum(p => p.maxAmount) * _oxygen_density;
            _maxCapacityHydrogenMass = partsThatContainHydrogen.Sum(p => p.maxAmount) * _hydrogen_density;
            _maxCapacityPeroxideMass = partsThatContainPeroxide.Sum(p => p.maxAmount) * _hydrogen_peroxide_density;

            _availableOxygenMass = partsThatContainOxygen.Sum(r => r.amount) * _oxygen_density;
            _availableHydrogenMass = partsThatContainHydrogen.Sum(r => r.amount) * _hydrogen_density;
            _spareRoomHydrogenPeroxideMass = partsThatContainPeroxide.Sum(r => r.maxAmount - r.amount) * _hydrogen_peroxide_density;

            // determine how much we can consume
            var fixedMaxOxygenConsumptionRate = _current_rate * _oxygenMassByFraction * fixedDeltaTime;
            var oxygenConsumptionRatio = fixedMaxOxygenConsumptionRate > 0 ? Math.Min(fixedMaxOxygenConsumptionRate, _availableOxygenMass) / fixedMaxOxygenConsumptionRate : 0;

            var fixedMaxHydrogenConsumptionRate = _current_rate * _hydrogenMassByFraction * fixedDeltaTime;
            var hydrogenConsumptionRatio = fixedMaxHydrogenConsumptionRate > 0 ? Math.Min(fixedMaxHydrogenConsumptionRate, _availableHydrogenMass) / fixedMaxHydrogenConsumptionRate : 0;

            _fixedConsumptionRate = _current_rate * fixedDeltaTime * Math.Min(oxygenConsumptionRatio, hydrogenConsumptionRatio);
            _consumptionRate = _fixedConsumptionRate / fixedDeltaTime;

            if (_fixedConsumptionRate > 0 && _spareRoomHydrogenPeroxideMass > 0)
            {
                var fixedMaxPossibleHydrogenPeroxidenRate = Math.Min(_spareRoomHydrogenPeroxideMass, _fixedConsumptionRate);

                var hydrogen_consumption_rate = fixedMaxPossibleHydrogenPeroxidenRate * _hydrogenMassByFraction;
                var oxygen_consumption_rate = fixedMaxPossibleHydrogenPeroxidenRate * _oxygenMassByFraction;

                // consume the resource
                _hydrogen_consumption_rate = _part.RequestResource(_hydrogenResourceName, hydrogen_consumption_rate / _hydrogen_density) / fixedDeltaTime * _hydrogen_density;
                _oxygen_consumption_rate = _part.RequestResource(_oxygenResourceName, oxygen_consumption_rate / _oxygen_density) / fixedDeltaTime * _oxygen_density;

                var combined_consumption_rate = (_hydrogen_consumption_rate + _oxygen_consumption_rate) * fixedDeltaTime / _hydrogen_peroxide_density;

                _hydrogen_peroxide_production_rate = -_part.RequestResource(_hydrogenPeroxideResourceName, -combined_consumption_rate) / fixedDeltaTime * _hydrogen_peroxide_density;
            }
            else
            {
                _hydrogen_consumption_rate = 0;
                _oxygen_consumption_rate = 0;
                _hydrogen_peroxide_production_rate = 0;
            }


            updateStatusMessage();
        }

        public override void UpdateGUI()
        {
            base.UpdateGUI();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Power", _bold_label, GUILayout.Width(labelWidth));
            GUILayout.Label(PluginHelper.getFormattedPowerString(CurrentPower) + "/" + PluginHelper.getFormattedPowerString(_effectiveMaxPower), _value_label, GUILayout.Width(valueWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Overal Consumption", _bold_label, GUILayout.Width(labelWidth));
            GUILayout.Label(((_consumptionRate * GameConstants.SECONDS_IN_HOUR).ToString("0.00000")) + " mT/hour", _value_label, GUILayout.Width(valueWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Hydrogen Available", _bold_label, GUILayout.Width(labelWidth));
            GUILayout.Label(_availableHydrogenMass.ToString("0.00000") + " mT / " + _maxCapacityHydrogenMass.ToString("0.00000") + " mT", _value_label, GUILayout.Width(valueWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Hydrogen Consumption Rate", _bold_label, GUILayout.Width(labelWidth));
            GUILayout.Label((_hydrogen_consumption_rate * GameConstants.SECONDS_IN_HOUR).ToString("0.00000") + " mT/hour", _value_label, GUILayout.Width(valueWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Oxygen Available", _bold_label, GUILayout.Width(labelWidth));
            GUILayout.Label(_availableOxygenMass.ToString("0.00000") + " mT / " + _maxCapacityOxygenMass.ToString("0.00000") + " mT", _value_label, GUILayout.Width(valueWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Oxygen Consumption Rate", _bold_label, GUILayout.Width(labelWidth));
            GUILayout.Label((_oxygen_consumption_rate * GameConstants.SECONDS_IN_HOUR).ToString("0.00000") + " mT/hour", _value_label, GUILayout.Width(valueWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Hydrogen Peroxide Storage", _bold_label, GUILayout.Width(labelWidth));
            GUILayout.Label(_spareRoomHydrogenPeroxideMass.ToString("0.00000") + " mT / " + _maxCapacityPeroxideMass.ToString("0.00000") + " mT", _value_label, GUILayout.Width(valueWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Hydrogen Peroxide Production Rate", _bold_label, GUILayout.Width(labelWidth));
            GUILayout.Label((_hydrogen_peroxide_production_rate * GameConstants.SECONDS_IN_HOUR).ToString("0.00000") + " mT/hour", _value_label, GUILayout.Width(valueWidth));
            GUILayout.EndHorizontal();
        }

        private void updateStatusMessage()
        {
            if (_hydrogen_peroxide_production_rate > 0)
                _status = "Electrolysing";
            else if (CurrentPower <= 0.01 * PowerRequirements)
                _status = "Insufficient Power";
            else
                _status = "Insufficient Storage";
        }

        public void PrintMissingResources() 
        {
            if (! _part.GetConnectedResources(InterstellarResourcesConfiguration.Instance.Hydrogen).Any(rs => rs.amount > 0) )
                ScreenMessages.PostScreenMessage("Missing " + InterstellarResourcesConfiguration.Instance.Hydrogen, 3.0f, ScreenMessageStyle.UPPER_CENTER);
            if (!_part.GetConnectedResources(InterstellarResourcesConfiguration.Instance.LqdOxygen).Any(rs => rs.amount > 0) )
                ScreenMessages.PostScreenMessage("Missing " + InterstellarResourcesConfiguration.Instance.LqdOxygen, 3.0f, ScreenMessageStyle.UPPER_CENTER);
        }
    }
}
