using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services.Forensics;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Tests
{
    /// <summary>
    /// Unit tests for TaskSchedulerService.
    /// </summary>
    [TestClass]
    public class TaskSchedulerServiceTests
    {
        private TaskSchedulerService _service = null!;

        [TestInitialize]
        public void Setup()
        {
            _service = new TaskSchedulerService();
            _service.ClearTasks(); // Ensure clean state for each test
        }

        [TestCleanup]
        public void Cleanup()
        {
            _service.Stop();
            _service.Dispose();
        }

        [TestMethod]
        public void OperationName_ShouldReturnCorrectName()
        {
            Assert.AreEqual("Task Scheduler", _service.OperationName);
        }

        [TestMethod]
        public void AddTask_ShouldAddTaskToList()
        {
            var task = new ScheduledTask
            {
                Name = "Test Task",
                TaskType = ScheduledTaskType.DiskCleanup,
                Frequency = ScheduleFrequency.Daily
            };

            _service.AddTask(task);

            Assert.AreEqual(1, _service.Tasks.Count);
            Assert.AreEqual("Test Task", _service.Tasks[0].Name);
        }

        [TestMethod]
        public void RemoveTask_ShouldRemoveTaskFromList()
        {
            var task = new ScheduledTask
            {
                Name = "Test Task",
                TaskType = ScheduledTaskType.TempFilesCleanup
            };

            _service.AddTask(task);
            Assert.AreEqual(1, _service.Tasks.Count);

            _service.RemoveTask(task.Id);
            Assert.AreEqual(0, _service.Tasks.Count);
        }

        [TestMethod]
        public void SetTaskEnabled_ShouldToggleTaskState()
        {
            var task = new ScheduledTask
            {
                Name = "Toggle Task",
                IsEnabled = true
            };

            _service.AddTask(task);
            
            _service.SetTaskEnabled(task.Id, false);
            Assert.IsFalse(_service.Tasks[0].IsEnabled);

            _service.SetTaskEnabled(task.Id, true);
            Assert.IsTrue(_service.Tasks[0].IsEnabled);
        }

        [TestMethod]
        public void GetTask_ShouldReturnCorrectTask()
        {
            var task1 = new ScheduledTask { Name = "Task 1" };
            var task2 = new ScheduledTask { Name = "Task 2" };

            _service.AddTask(task1);
            _service.AddTask(task2);

            var retrieved = _service.GetTask(task2.Id);

            Assert.IsNotNull(retrieved);
            Assert.AreEqual("Task 2", retrieved.Name);
        }

        [TestMethod]
        public void GetTask_WithInvalidId_ShouldReturnNull()
        {
            var result = _service.GetTask("nonexistent-id");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void StartStop_ShouldNotThrow()
        {
            _service.Start();
            _service.Stop();
            // Should not throw
        }

        [TestMethod]
        public async Task RunTaskAsync_WithInvalidId_ShouldReturnFailure()
        {
            var result = await _service.RunTaskAsync("invalid-id");

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Task not found", result.Message);
        }

        [TestMethod]
        public void UpdateTask_ShouldModifyExistingTask()
        {
            var task = new ScheduledTask { Name = "Original" };
            _service.AddTask(task);

            task.Name = "Updated";
            _service.UpdateTask(task);

            Assert.AreEqual("Updated", _service.Tasks[0].Name);
        }
    }
}
