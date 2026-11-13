using System;
using Moq;
using UniSolder.Core;
using UniSolder.Desktop.Services;
using UniSolder.Desktop.ViewModels;
using Xunit;

namespace UniSolder.Desktop.Tests.ViewModels
{
    public class TelemetryChartViewModelTests
    {
        [Fact]
        public void InitialState_Has256PointBuffersAndDefaultSeries()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            using var vm = new TelemetryChartViewModel(mockService.Object);

            Assert.Equal(256, vm.CurrentTempData.Length);
            Assert.Equal(256, vm.TargetTempData.Length);
            Assert.Equal(256, vm.DutyData.Length);
            Assert.Equal(0, vm.SampleCount);
            Assert.Equal(0, vm.CurrentIndex);
            Assert.False(vm.IsPaused);
            Assert.Equal(10, vm.SeriesList.Count);
        }

        [Fact]
        public void AddTelemetryData_UpdatesBuffersAndLiveValues()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            using var vm = new TelemetryChartViewModel(mockService.Object);

            var telemetry = new TelemetryData
            {
                TipTemp = 320.5f,
                ColdJunctionTemp = 25.0f,
                AdcTemp = 318.0f,
                TargetAvgFiltered = 320.0f,
                TargetAvgPower = 45.0f,
                HeaterResistance = 1250.0f,
                Duty = 85.5f,
                IsPowerOn = true,
                DestinationReached = 100.0f,
                WsDelta = new float[] { 210.5f }
            };

            vm.AddTelemetryData(telemetry);

            Assert.Equal(1, vm.SampleCount);
            Assert.Equal(1, vm.CurrentIndex);

            Assert.Equal(320.5, vm.CurrentTempData[1], 1);
            Assert.Equal(25.0, vm.TargetTempData[1], 1);
            Assert.Equal(85.5, vm.DutyData[1], 1);
            Assert.Equal(100.0, vm.PowerData[1], 1);

            // Verify live values in SeriesList
            var cTempSeries = Assert.Single(vm.SeriesList, s => s.Key == "CTemp");
            Assert.Equal(320.5, cTempSeries.CurrentValue, 1);

            var dutySeries = Assert.Single(vm.SeriesList, s => s.Key == "Duty");
            Assert.Equal(85.5, dutySeries.CurrentValue, 1);
        }

        [Fact]
        public void CircularBuffer_WrapsAt256SamplesWithoutOverflow()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            using var vm = new TelemetryChartViewModel(mockService.Object);

            for (int i = 0; i < 300; i++)
            {
                var telemetry = new TelemetryData
                {
                    TipTemp = i,
                    Duty = i % 100
                };
                vm.AddTelemetryData(telemetry);
            }

            Assert.Equal(300, vm.SampleCount);
            Assert.Equal(300 % 256, vm.CurrentIndex);
            Assert.Equal(299, vm.CurrentTempData[vm.CurrentIndex], 1);
        }

        [Fact]
        public void ClearData_ResetsAllBuffersAndCounters()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            using var vm = new TelemetryChartViewModel(mockService.Object);

            vm.AddTelemetryData(new TelemetryData { TipTemp = 350, Duty = 100 });
            Assert.Equal(1, vm.SampleCount);

            vm.ClearDataCommand.Execute(null);

            Assert.Equal(0, vm.SampleCount);
            Assert.Equal(0, vm.CurrentIndex);
            Assert.All(vm.CurrentTempData, val => Assert.Equal(0, val));
            Assert.All(vm.SeriesList, s => Assert.Equal(0, s.CurrentValue));
        }

        [Fact]
        public void TogglePause_PausesTelemetryIngestion()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            using var vm = new TelemetryChartViewModel(mockService.Object);

            vm.TogglePauseCommand.Execute(null);
            Assert.True(vm.IsPaused);

            // Raise telemetry event from service while paused
            mockService.Raise(s => s.TelemetryReceived += null, mockService.Object, new TelemetryData { TipTemp = 400 });

            Assert.Equal(0, vm.SampleCount);
            Assert.Equal(0, vm.CurrentIndex);
        }
    }
}
