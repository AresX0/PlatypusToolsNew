using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.ViewModels;
using System.Threading;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="StatusBarViewModel"/> - status bar display and progress tracking.
    /// </summary>
    [TestClass]
    public class StatusBarViewModelTests
    {
        #region Instance Tests

        [TestMethod]
        public void Instance_ReturnsNonNull()
        {
            Assert.IsNotNull(StatusBarViewModel.Instance);
        }

        [TestMethod]
        public void Instance_ReturnsSameInstance()
        {
            var instance1 = StatusBarViewModel.Instance;
            var instance2 = StatusBarViewModel.Instance;
            
            Assert.AreSame(instance1, instance2);
        }

        #endregion

        #region Default Values Tests

        [TestMethod]
        public void StatusMessage_DefaultIsReady()
        {
            var vm = StatusBarViewModel.Instance;
            vm.Reset();
            
            Assert.AreEqual("Ready", vm.StatusMessage);
        }

        [TestMethod]
        public void Progress_DefaultIsZero()
        {
            var vm = StatusBarViewModel.Instance;
            vm.Reset();
            
            Assert.AreEqual(0, vm.Progress);
        }

        [TestMethod]
        public void IsOperationRunning_DefaultIsFalse()
        {
            var vm = StatusBarViewModel.Instance;
            vm.Reset();
            
            Assert.IsFalse(vm.IsOperationRunning);
        }

        [TestMethod]
        public void IsIndeterminate_DefaultIsFalse()
        {
            var vm = StatusBarViewModel.Instance;
            vm.Reset();
            
            Assert.IsFalse(vm.IsIndeterminate);
        }

        #endregion

        #region Property Setting Tests

        [TestMethod]
        public void StatusMessage_CanBeSet()
        {
            var vm = StatusBarViewModel.Instance;
            vm.StatusMessage = "Processing...";
            
            Assert.AreEqual("Processing...", vm.StatusMessage);
            
            // Reset
            vm.Reset();
        }

        [TestMethod]
        public void Progress_CanBeSet()
        {
            var vm = StatusBarViewModel.Instance;
            vm.Progress = 50;
            
            Assert.AreEqual(50, vm.Progress);
            
            // Reset
            vm.Reset();
        }

        [TestMethod]
        public void Progress_ClampedToZeroTo100()
        {
            var vm = StatusBarViewModel.Instance;
            
            vm.Progress = -10;
            Assert.AreEqual(0, vm.Progress);
            
            vm.Progress = 150;
            Assert.AreEqual(100, vm.Progress);
            
            // Reset
            vm.Reset();
        }

        [TestMethod]
        public void OperationName_CanBeSet()
        {
            var vm = StatusBarViewModel.Instance;
            vm.OperationName = "Converting video...";
            
            Assert.AreEqual("Converting video...", vm.OperationName);
            
            // Reset
            vm.Reset();
        }

        [TestMethod]
        public void ItemsProcessed_CanBeSet()
        {
            var vm = StatusBarViewModel.Instance;
            vm.ItemsProcessed = 5;
            
            Assert.AreEqual(5, vm.ItemsProcessed);
            
            // Reset
            vm.Reset();
        }

        [TestMethod]
        public void TotalItems_CanBeSet()
        {
            var vm = StatusBarViewModel.Instance;
            vm.TotalItems = 100;
            
            Assert.AreEqual(100, vm.TotalItems);
            
            // Reset
            vm.Reset();
        }

        #endregion

        #region StartOperation Tests

        [TestMethod]
        public void StartOperation_SetsOperationName()
        {
            var vm = StatusBarViewModel.Instance;
            vm.StartOperation("Test Operation");
            
            Assert.AreEqual("Test Operation", vm.OperationName);
            
            vm.Reset();
        }

        [TestMethod]
        public void StartOperation_SetsIsOperationRunningTrue()
        {
            var vm = StatusBarViewModel.Instance;
            vm.StartOperation("Test Operation");
            
            Assert.IsTrue(vm.IsOperationRunning);
            
            vm.Reset();
        }

        [TestMethod]
        public void StartOperation_ResetsProgress()
        {
            var vm = StatusBarViewModel.Instance;
            vm.Progress = 50;
            vm.StartOperation("New Operation");
            
            Assert.AreEqual(0, vm.Progress);
            
            vm.Reset();
        }

        [TestMethod]
        public void StartOperation_SetsIndeterminate_WhenNoTotalItems()
        {
            var vm = StatusBarViewModel.Instance;
            vm.StartOperation("Scanning...", totalItems: 0);
            
            Assert.IsTrue(vm.IsIndeterminate);
            
            vm.Reset();
        }

        [TestMethod]
        public void StartOperation_NotIndeterminate_WhenTotalItemsSpecified()
        {
            var vm = StatusBarViewModel.Instance;
            vm.StartOperation("Processing...", totalItems: 100);
            
            Assert.IsFalse(vm.IsIndeterminate);
            Assert.AreEqual(100, vm.TotalItems);
            
            vm.Reset();
        }

        #endregion

        #region UpdateProgress Tests

        [TestMethod]
        public void UpdateProgress_SetsItemsProcessed()
        {
            var vm = StatusBarViewModel.Instance;
            vm.StartOperation("Processing...", totalItems: 100);
            vm.UpdateProgress(25);
            
            Assert.AreEqual(25, vm.ItemsProcessed);
            
            vm.Reset();
        }

        [TestMethod]
        public void UpdateProgress_CalculatesProgressPercentage()
        {
            var vm = StatusBarViewModel.Instance;
            vm.StartOperation("Processing...", totalItems: 100);
            vm.UpdateProgress(50);
            
            Assert.AreEqual(50, vm.Progress);
            
            vm.Reset();
        }

        [TestMethod]
        public void UpdateProgress_UpdatesStatusMessage_WhenProvided()
        {
            var vm = StatusBarViewModel.Instance;
            vm.StartOperation("Processing...", totalItems: 100);
            vm.UpdateProgress(25, "Processing file 25 of 100");
            
            Assert.AreEqual("Processing file 25 of 100", vm.StatusMessage);
            
            vm.Reset();
        }

        #endregion

        #region CancellationToken Tests

        [TestMethod]
        public void GetCancellationToken_ReturnsValidToken()
        {
            var vm = StatusBarViewModel.Instance;
            var token = vm.GetCancellationToken();
            
            Assert.IsFalse(token.IsCancellationRequested);
            
            vm.Reset();
        }

        [TestMethod]
        public void GetCancellationToken_ReturnsNewTokenEachTime()
        {
            var vm = StatusBarViewModel.Instance;
            var token1 = vm.GetCancellationToken();
            var token2 = vm.GetCancellationToken();
            
            // They should be different tokens
            Assert.AreNotEqual(token1, token2);
            
            vm.Reset();
        }

        #endregion

        #region CancelCommand Tests

        [TestMethod]
        public void CancelCommand_IsNotNull()
        {
            var vm = StatusBarViewModel.Instance;
            Assert.IsNotNull(vm.CancelCommand);
        }

        #endregion
    }
}
