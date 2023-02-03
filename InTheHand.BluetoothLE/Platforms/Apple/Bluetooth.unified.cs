﻿//-----------------------------------------------------------------------
// <copyright file="Bluetooth.unified.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-23 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using CoreBluetooth;
using Foundation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
#if __IOS__
using CoreGraphics;
#endif
#if !__MACOS__
using UIKit;
#endif

namespace InTheHand.Bluetooth
{
    partial class Bluetooth
    {
        internal static CBCentralManager _manager;
        private static BluetoothDelegate _delegate = new BluetoothDelegate();
        private static bool availability = false;

        static Bluetooth()
        {
            Debug.WriteLine("Bluetooth_ctor");
            CBCentralInitOptions options = new CBCentralInitOptions { ShowPowerAlert = true
#if __IOS__
//                ,  RestoreIdentifier = NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleName")?.ToString()
#endif
            };

            _manager = new CBCentralManager(_delegate, null);//, options);
            Debug.WriteLine($"CBCentralManager:{_manager.State}");
        }

        internal static event EventHandler UpdatedState;
        internal static event EventHandler<CBPeripheralEventArgs> ConnectedPeripheral;
        internal static event EventHandler<CBPeripheralErrorEventArgs> DisconnectedPeripheral;
        internal static event EventHandler<CBDiscoveredPeripheralEventArgs> DiscoveredPeripheral;
        internal static event EventHandler<CBPeripheralErrorEventArgs> FailedToConnectPeripheral;

        private sealed class BluetoothDelegate : CBCentralManagerDelegate
        {

            internal BluetoothDelegate()
            {
            }

            public override void ConnectionEventDidOccur(CBCentralManager central, CBConnectionEvent connectionEvent, CBPeripheral peripheral)
            {
                base.ConnectionEventDidOccur(central, connectionEvent, peripheral);
            }

            public override void DidUpdateAncsAuthorization(CBCentralManager central, CBPeripheral peripheral)
            {
                base.DidUpdateAncsAuthorization(central, peripheral);
            }

            public override void UpdatedState(CBCentralManager central)
            {
                Debug.WriteLine($"CBCentralManager.UpdatedState:{central.State}");
                Bluetooth.UpdatedState?.Invoke(central, EventArgs.Empty);

                bool newAvailability = false;

                switch(central.State)
                {
#if NET6_0_OR_GREATER  || __WATCHOS__
                    case CBManagerState.PoweredOn:
                    case CBManagerState.Resetting:
#else
                    case CBCentralManagerState.PoweredOn:
                    case CBCentralManagerState.Resetting:
#endif
                        newAvailability = true;
                        break;

                    default:
                        newAvailability = false;
                        break;
                }

                if(availability != newAvailability)
                {
                    availability = newAvailability;
                    OnAvailabilityChanged();
                }
            }

            public override void ConnectedPeripheral(CBCentralManager central, CBPeripheral peripheral)
            {
                System.Diagnostics.Debug.WriteLine($"Connected {peripheral.Identifier}");
                Bluetooth.ConnectedPeripheral?.Invoke(central, new CBPeripheralEventArgs(peripheral));
            }

            public override void DisconnectedPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError error)
            {
                System.Diagnostics.Debug.WriteLine($"Disconnected {peripheral.Identifier} {error}");
                Bluetooth.DisconnectedPeripheral?.Invoke(central, new CBPeripheralErrorEventArgs(peripheral, error));
            }

            public override void FailedToConnectPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError error)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to connect {peripheral.Identifier} {error.Code}");
                Bluetooth.FailedToConnectPeripheral?.Invoke(central, new CBPeripheralErrorEventArgs(peripheral, error));
            }

            public override void DiscoveredPeripheral(CBCentralManager central, CBPeripheral peripheral, NSDictionary advertisementData, NSNumber RSSI)
            {
                OnDiscoveredPeripheral(central, peripheral, advertisementData, RSSI);
            }

#if __IOS__
            public override void WillRestoreState(CBCentralManager central, NSDictionary dict)
            {
                base.WillRestoreState(central, dict);
            }
#endif
        }

        private static ObservableCollection<BluetoothDevice> _foundDevices = new ObservableCollection<BluetoothDevice>();

        
        private static void OnDiscoveredPeripheral(CBCentralManager central, CBPeripheral peripheral, NSDictionary advertisementData, NSNumber RSSI)
        {
            DiscoveredPeripheral?.Invoke(central, new CBDiscoveredPeripheralEventArgs(peripheral, advertisementData, RSSI));
            var e = new BluetoothAdvertisingEvent(peripheral, advertisementData, RSSI);
            AdvertisementReceived?.Invoke(central, e);
            if(!_foundDevices.Contains(peripheral))
            {
                Debug.WriteLine($"Peripheral: {peripheral.Identifier} Name: {peripheral.Name} RSSI: {RSSI}");
                _foundDevices.Add(peripheral);
            }
        }

#if __IOS__
        internal static event EventHandler<CBPeripheral[]> OnRetrievedPeripherals;
#endif


        static Task<bool> PlatformGetAvailability()
        {
            bool available = false;

            System.Diagnostics.Debug.WriteLine($"GetAvailability:{_manager.State}");

            switch(_manager.State)
            {
#if NET6_0_OR_GREATER || __WATCHOS__
                case CBManagerState.PoweredOn:
                case CBManagerState.Resetting:
#else
                case CBCentralManagerState.PoweredOn:
                case CBCentralManagerState.Resetting:             
#endif
                    available = true;
                    break;
            }

            return Task.FromResult(available);
        }

#if __IOS__
        private static UIAlertController controller = null;
#endif

        private static async Task WaitForState()
        {
#if NET6_0_OR_GREATER || __WATCHOS__
            if (_manager.State == CBManagerState.PoweredOn)
#else
            if (_manager.State == CBCentralManagerState.PoweredOn)
#endif
                return;

            System.Diagnostics.Debug.WriteLine("Waiting for CBCentralManager.State");
            TaskCompletionSource<bool> tcsStatus = new TaskCompletionSource<bool>();

            void Handler(object sender, EventArgs e)
            {
                System.Diagnostics.Debug.WriteLine(_manager.State);
#if NET6_0_OR_GREATER  || __WATCHOS__
                if (_manager.State == CBManagerState.PoweredOn)
#else
                if (_manager.State == CBCentralManagerState.PoweredOn)
#endif
                {
                    tcsStatus.SetResult(true);
                    _manager.UpdatedState -= Handler;
                }
            };

            _manager.UpdatedState += Handler;

            await tcsStatus.Task;
        }
        static async Task<BluetoothDevice> PlatformRequestDevice(RequestDeviceOptions options)
        {
            await WaitForState();

#if __IOS__
            TaskCompletionSource<BluetoothDevice> tcs = new TaskCompletionSource<BluetoothDevice>();

#if NET6_0_OR_GREATER
            if (_manager.State != CBManagerState.PoweredOn)
#else
            if(_manager.State != CBCentralManagerState.PoweredOn)
#endif
            {
                throw new InvalidOperationException();
            }

            controller = UIAlertController.Create("Select a Bluetooth accessory", null, UIAlertControllerStyle.Alert);
            controller.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, (a)=> {
                tcs.SetResult(null);
                StopScanning();
                Debug.WriteLine(a == null ? "<null>" : a.ToString());
            }));
            
            CGRect rect = new CGRect(0, 0, 272, 272);
            var tvc = new UITableViewController(UITableViewStyle.Plain)
            {
                PreferredContentSize = rect.Size
            };
            controller.PreferredContentSize = rect.Size;
            var source = new InTheHand.Bluetooth.Platforms.Apple.BluetoothTableViewSource(options);
            source.DeviceSelected += (s, e) =>
             {
                 tvc.DismissViewController(true, null);
                 tcs.SetResult(e);
             };

            tvc.TableView.Delegate = source;
            tvc.TableView.DataSource = source;

            tvc.TableView.UserInteractionEnabled = true;
            tvc.TableView.AllowsSelection = true;
            controller.SetValueForKey(tvc, new Foundation.NSString("contentViewController"));

            UIViewController currentController = UIApplication.SharedApplication.KeyWindow.RootViewController;
            while (currentController.PresentedViewController != null)
                currentController = currentController.PresentedViewController;

            currentController.PresentViewController(controller, true, null);

            return await tcs.Task;
#else
            return null;
#endif
        }

        static Task<IReadOnlyCollection<BluetoothDevice>> PlatformScanForDevices(RequestDeviceOptions options, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyCollection<BluetoothDevice>)new List<BluetoothDevice>().AsReadOnly());
        }

        static async Task<IReadOnlyCollection<BluetoothDevice>> PlatformGetPairedDevices()
        {
            await WaitForState();
#if __IOS__
            PairedDeviceHandler deviceHandler = new PairedDeviceHandler();
            OnRetrievedPeripherals += deviceHandler.OnRetrievedPeripherals;
            List<BluetoothDevice> devices = new List<BluetoothDevice>();
            var periphs = _manager.RetrieveConnectedPeripherals(GattServiceUuids.GenericAccess, GattServiceUuids.GenericAttribute, GattServiceUuids.DeviceInformation, GattServiceUuids.Battery);
            foreach (var p in periphs)
            {
                devices.Add(p);
            }

            return (IReadOnlyCollection<BluetoothDevice>)devices.AsReadOnly();
#else
            return (IReadOnlyCollection<BluetoothDevice>)new List<BluetoothDevice>().AsReadOnly();
#endif
        }

#if __IOS__
        private class PairedDeviceHandler
        {
            EventWaitHandle handle = new EventWaitHandle(false, EventResetMode.ManualReset);
            List<BluetoothDevice> devices = new List<BluetoothDevice>();

            public IReadOnlyCollection<BluetoothDevice> Devices
            {
                get
                {
                    return devices.AsReadOnly();
                }
            }

            public void WaitOne()
            {
                handle.WaitOne();
            }

            public void OnRetrievedPeripherals(object sender, CBPeripheral[] peripherals)
            {
                foreach (var peripheral in peripherals)
                {
                    devices.Add(peripheral);
                }

                handle.Set();
            }
        }
#endif

        private static async Task<BluetoothLEScan> PlatformRequestLEScan(BluetoothLEScanOptions options)
        {
            return new BluetoothLEScan(options);
        }

        private static int _scanCount = 0;
        internal static void StartScanning(CBUUID[] services)
        {
            _scanCount++;
            System.Diagnostics.Debug.WriteLine($"StartScanning count:{_scanCount}");

            if (!_manager.IsScanning)
                _manager.ScanForPeripherals(services, new PeripheralScanningOptions { AllowDuplicatesKey = true });
        }

        internal static void StopScanning()
        {
            _scanCount--;

            System.Diagnostics.Debug.WriteLine($"StopScanning count:{_scanCount}");
            if (_scanCount == 0 && _manager.IsScanning)
                _manager.StopScan();
        }

        private static bool _oldAvailability;

        private static async void AddAvailabilityChanged()
        {
            _oldAvailability = await PlatformGetAvailability();
            _manager.UpdatedState += _manager_UpdatedState;
        }

        private static void RemoveAvailabilityChanged()
        {
            _manager.UpdatedState -= _manager_UpdatedState;
        }

        private static async void _manager_UpdatedState(object sender, EventArgs e)
        {
            bool newAvailability = await PlatformGetAvailability();
            if (newAvailability != _oldAvailability)
            {
                _oldAvailability = newAvailability;
                OnAvailabilityChanged();
            }
        }
    }
}
