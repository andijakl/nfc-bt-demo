// Copyright 2015 Andreas Jakl, Tieto Corporation. All rights reserved. 
// https://github.com/andijakl/nfc-bt-demo
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Devices.SmartCards;
using Windows.Foundation.Metadata;
using Windows.Networking.Proximity;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using NdefLibrary.Ndef;

namespace NfcBtDemo
{
    /// <summary>
    /// Example app for NFC, Smart Cards and Bluetooth Beacons with Windows 10.
    /// By Andreas Jakl
    /// https://twitter.com/andijakl
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // NFC
        private ProximityDevice _device;
        private long _subscribedMessageId;

        // Smart Card
        private SmartCardReader _smartCardReader;

        // Bluetooth Beacons
        private BluetoothLEAdvertisementWatcher _watcher;
        private BluetoothLEAdvertisementPublisher _publisher;

        // UI
        private readonly CoreDispatcher _dispatcher;

        public MainPage()
        {
            this.InitializeComponent();
            _dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;
        }

        #region NDEF
        private void NdefButton_Click(object sender, RoutedEventArgs e)
        {
            if (_device != null) return;
            // Start NDEF subscription
            _device = ProximityDevice.GetDefault();
            _subscribedMessageId = _device.SubscribeForMessage("NDEF", MessageReceivedHandler);
            SetStatusOutput("Subscribed for NDEF / NFC");
        }

        private void MessageReceivedHandler(ProximityDevice sender, ProximityMessage message)
        {
            SetStatusOutput("Found proximity card");
            // Convert to NdefMessage from NDEF / NFC Library
            var msgArray = message.Data.ToArray();
            NdefMessage ndefMessage = NdefMessage.FromByteArray(msgArray);
            // Loop over all records contained in the message
            foreach (NdefRecord record in ndefMessage)
            {
                // Check the type of each record 
                if (record.CheckSpecializedType(false) == typeof(NdefUriRecord))
                {
                    // Convert and extract URI info
                    var uriRecord = new NdefUriRecord(record);
                    SetStatusOutput("NDEF URI: " + uriRecord.Uri);
                }
            }
        }
        #endregion

        #region Smart Card
        private void SmartCardButton_Click(object sender, RoutedEventArgs e)
        {
            _dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => await InitSmartCardAsync());
        }

        private async Task InitSmartCardAsync()
        {
            // Check if the SmartCardConnection API exists on this currently running SKU of Windows
            if (!ApiInformation.IsTypePresent("Windows.Devices.SmartCards.SmartCardConnection"))
            {
                // This SKU of Windows does not support Smart Card Connections
                SetStatusOutput("This SKU of Windows does not support Smart Card connections");
                return;
            }

            // Initialize smart card reader
            var devSelector = SmartCardReader.GetDeviceSelector(SmartCardReaderKind.Nfc);
            var devices = await DeviceInformation.FindAllAsync(devSelector);

            if (devices != null && devices.Count == 0)
            {
                SetStatusOutput("No NFC Smart Card Reader found in this device.");
                return;
            }

            // Subscribe to Smart Cards
            _smartCardReader = await SmartCardReader.FromIdAsync(devices.FirstOrDefault().Id);
            _smartCardReader.CardAdded += SmartCardReaderOnCardAdded;
            SetStatusOutput("Subscribed for NFC Smart Cards");
        }

        private async void SmartCardReaderOnCardAdded(SmartCardReader sender, CardAddedEventArgs args)
        {
            SetStatusOutput("Found smart card");

            // Get Answer to Reset (ATR) according to ISO 7816
            // ATR = info about smart card's characteristics, behaviors, and state
            // https://en.wikipedia.org/wiki/Answer_to_reset
            var info = await args.SmartCard.GetAnswerToResetAsync();
            var infoArray = info.ToArray();
            SetStatusOutput("Answer to Reset: " + BitConverter.ToString(infoArray));

            // Connect to the card
            // var connection = await args.SmartCard.ConnectAsync();
            // ...
        }

        #endregion

        #region Bluetooth Beacons

        private void BeaconWatchButton_Click(object sender, RoutedEventArgs e)
        {
            _watcher = new BluetoothLEAdvertisementWatcher();

            // Manufacturer specific data to customize
            var writer = new DataWriter();
            const ushort uuidData = 0x1234;
            writer.WriteUInt16(uuidData);
            var manufacturerData = new BluetoothLEManufacturerData
            {
                CompanyId = 0xFFFE,
                Data = writer.DetachBuffer()
            };
            _watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(manufacturerData);

            // Start watching
            _watcher.Received += WatcherOnReceived;
            _watcher.Start();
            SetStatusOutput("Watching for Bluetooth Beacons");
        }

        private void WatcherOnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            // Signal strength
            var rssi = eventArgs.RawSignalStrengthInDBm;

            // Extract manufacturer data (if present)
            var manufacturerDataString = string.Empty;
            if (eventArgs.Advertisement.ManufacturerData.Any())
            {
                var manufacturerData = eventArgs.Advertisement.ManufacturerData.First();

                var data = new byte[manufacturerData.Data.Length];
                using (var reader = DataReader.FromBuffer(manufacturerData.Data))
                {
                    reader.ReadBytes(data);
                }

                // Print the company ID + the raw data in hex format
                manufacturerDataString = $"0x{manufacturerData.CompanyId.ToString("X")}: {BitConverter.ToString(data)}";
            }
            SetStatusOutput($"Beacon detected (Strength: {rssi}): {manufacturerDataString}");
        }

        private void BeaconPublishButton_Click(object sender, RoutedEventArgs e)
        {
            _publisher = new BluetoothLEAdvertisementPublisher();

            // Manufacturer specific data to customize
            var writer = new DataWriter();
            const ushort uuidData = 0x1234; // Custom payload
            writer.WriteUInt16(uuidData);
            var manufacturerData = new BluetoothLEManufacturerData
            {
                CompanyId = 0xFFFE,         // Custom manufacturer
                Data = writer.DetachBuffer()
            };
            _publisher.Advertisement.ManufacturerData.Add(manufacturerData);

            // Start publishing
            _publisher.Start();
            SetStatusOutput("Publishing Bluetooth Beacon");
        }

        #endregion

        #region UI

        private void SetStatusOutput(string newStatus)
        {
            // Update the status output UI element in the UI thread
            // (some of the callbacks are in a different thread that wouldn't be allowed
            // to modify the UI thread)
            _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { if (StatusOutput != null) StatusOutput.Text += "\n" + newStatus; });
        }

        #endregion
    }
}
