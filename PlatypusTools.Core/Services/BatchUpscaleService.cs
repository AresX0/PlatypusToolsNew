using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.ImageScaler;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for batch upscaling images with queue management.
    /// </summary>
    public class BatchUpscaleService
    {
        private static BatchUpscaleService? _instance;
        public static BatchUpscaleService Instance => _instance ??= new BatchUpscaleService();
        
        private readonly ConcurrentQueue<BatchUpscaleJob> _jobQueue = new();
        private readonly List<BatchUpscaleJob> _allJobs = new();
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private bool _isProcessing;
        
        public event EventHandler<BatchUpscaleJob>? JobStarted;
        public event EventHandler<BatchUpscaleJob>? JobCompleted;
        public event EventHandler<BatchUpscaleItem>? ItemStarted;
        public event EventHandler<BatchUpscaleItem>? ItemCompleted;
        public event EventHandler<(BatchUpscaleItem Item, string Error)>? ItemFailed;
        public event EventHandler<double>? OverallProgressChanged;
        
        public IReadOnlyList<BatchUpscaleJob> AllJobs
        {
            get { lock (_lock) { return _allJobs.ToList(); } }
        }
        
        public bool IsProcessing => _isProcessing;
        public int QueuedJobCount => _jobQueue.Count;
        
        /// <summary>
        /// Creates a new batch job from a list of files.
        /// </summary>
        public BatchUpscaleJob CreateJob(string name, IEnumerable<string> filePaths, BatchUpscaleSettings settings)
        {
            var job = new BatchUpscaleJob
            {
                Name = name,
                Settings = settings,
                Items = filePaths.Select(path => new BatchUpscaleItem
                {
                    SourcePath = path,
                    OutputPath = GetOutputPath(path, settings)
                }).ToList()
            };
            
            lock (_lock)
            {
                _allJobs.Add(job);
            }
            
            return job;
        }
        
        /// <summary>
        /// Adds a job to the processing queue.
        /// </summary>
        public void EnqueueJob(BatchUpscaleJob job)
        {
            job.Status = BatchJobStatus.Queued;
            _jobQueue.Enqueue(job);
            
            if (!_isProcessing)
            {
                _ = StartProcessingAsync();
            }
        }
        
        /// <summary>
        /// Starts processing the job queue.
        /// </summary>
        public async Task StartProcessingAsync()
        {
            if (_isProcessing) return;
            
            _isProcessing = true;
            _cts = new CancellationTokenSource();
            
            try
            {
                while (_jobQueue.TryDequeue(out var job) && !_cts.Token.IsCancellationRequested)
                {
                    await ProcessJobAsync(job, _cts.Token);
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }
        
        /// <summary>
        /// Stops all processing.
        /// </summary>
        public void StopProcessing()
        {
            _cts?.Cancel();
        }
        
        /// <summary>
        /// Cancels a specific job.
        /// </summary>
        public void CancelJob(BatchUpscaleJob job)
        {
            job.Status = BatchJobStatus.Cancelled;
            foreach (var item in job.Items.Where(i => i.Status == BatchJobStatus.Queued || i.Status == BatchJobStatus.Processing))
            {
                item.Status = BatchJobStatus.Cancelled;
            }
        }
        
        /// <summary>
        /// Removes a job from history.
        /// </summary>
        public void RemoveJob(BatchUpscaleJob job)
        {
            lock (_lock)
            {
                _allJobs.Remove(job);
            }
        }
        
        /// <summary>
        /// Clears completed jobs from history.
        /// </summary>
        public void ClearCompletedJobs()
        {
            lock (_lock)
            {
                _allJobs.RemoveAll(j => j.Status == BatchJobStatus.Completed || j.Status == BatchJobStatus.Cancelled);
            }
        }
        
        private async Task ProcessJobAsync(BatchUpscaleJob job, CancellationToken ct)
        {
            job.Status = BatchJobStatus.Processing;
            job.StartedAt = DateTime.Now;
            JobStarted?.Invoke(this, job);
            
            var maxConcurrent = job.Settings.MaxConcurrentJobs;
            using var semaphore = new SemaphoreSlim(maxConcurrent);
            var tasks = new List<Task>();
            
            foreach (var item in job.Items)
            {
                if (ct.IsCancellationRequested) break;
                
                await semaphore.WaitAsync(ct);
                
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessItemAsync(item, job.Settings, ct);
                        
                        lock (job)
                        {
                            if (item.Status == BatchJobStatus.Completed)
                            {
                                job.CompletedCount++;
                            }
                            else if (item.Status == BatchJobStatus.Failed)
                            {
                                job.FailedCount++;
                            }
                        }
                        
                        UpdateJobProgress(job);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct);
                
                tasks.Add(task);
            }
            
            await Task.WhenAll(tasks);
            
            job.Status = ct.IsCancellationRequested 
                ? BatchJobStatus.Cancelled 
                : (job.FailedCount == job.Items.Count ? BatchJobStatus.Failed : BatchJobStatus.Completed);
            job.CompletedAt = DateTime.Now;
            
            JobCompleted?.Invoke(this, job);
        }
        
        private async Task ProcessItemAsync(BatchUpscaleItem item, BatchUpscaleSettings settings, CancellationToken ct)
        {
            item.Status = BatchJobStatus.Processing;
            item.StartedAt = DateTime.Now;
            ItemStarted?.Invoke(this, item);
            
            try
            {
                // Get original image info
                var sourceInfo = new FileInfo(item.SourcePath);
                item.OriginalSize = sourceInfo.Length;
                
                // Get dimensions (simplified - in real impl would use image library)
                await GetImageDimensionsAsync(item);
                
                // Calculate output dimensions
                if (settings.TargetWidth.HasValue && settings.TargetHeight.HasValue)
                {
                    item.OutputWidth = settings.TargetWidth.Value;
                    item.OutputHeight = settings.TargetHeight.Value;
                }
                else
                {
                    item.OutputWidth = (int)(item.OriginalWidth * settings.ScaleFactor);
                    item.OutputHeight = (int)(item.OriginalHeight * settings.ScaleFactor);
                }
                
                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(item.OutputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                // Check for existing file
                if (File.Exists(item.OutputPath) && !settings.OverwriteExisting)
                {
                    throw new IOException($"Output file already exists: {item.OutputPath}");
                }
                
                // Perform upscaling based on mode
                await UpscaleImageAsync(item, settings, ct);
                
                // Get output file info
                if (File.Exists(item.OutputPath))
                {
                    item.OutputSize = new FileInfo(item.OutputPath).Length;
                }
                
                item.Status = BatchJobStatus.Completed;
                item.Progress = 100;
                item.CompletedAt = DateTime.Now;
                
                ItemCompleted?.Invoke(this, item);
            }
            catch (OperationCanceledException)
            {
                item.Status = BatchJobStatus.Cancelled;
            }
            catch (Exception ex)
            {
                item.Status = BatchJobStatus.Failed;
                item.ErrorMessage = ex.Message;
                item.CompletedAt = DateTime.Now;
                
                ItemFailed?.Invoke(this, (item, ex.Message));
            }
        }
        
        private async Task GetImageDimensionsAsync(BatchUpscaleItem item)
        {
            // Use FFprobe or similar to get image dimensions
            // For now, use placeholder
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{item.SourcePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    
                    var parts = output.Trim().Split(',');
                    if (parts.Length == 2)
                    {
                        item.OriginalWidth = int.TryParse(parts[0], out var w) ? w : 0;
                        item.OriginalHeight = int.TryParse(parts[1], out var h) ? h : 0;
                    }
                }
            }
            catch
            {
                // Fall back to default dimensions
                item.OriginalWidth = 1920;
                item.OriginalHeight = 1080;
            }
        }
        
        private async Task UpscaleImageAsync(BatchUpscaleItem item, BatchUpscaleSettings settings, CancellationToken ct)
        {
            // Choose upscaling method based on mode
            switch (settings.Mode)
            {
                case UpscaleMode.RealESRGAN:
                case UpscaleMode.ESRGAN:
                case UpscaleMode.BSRGAN:
                case UpscaleMode.SwinIR:
                    await UpscaleWithAIAsync(item, settings, ct);
                    break;
                    
                case UpscaleMode.GFPGAN:
                case UpscaleMode.CodeFormer:
                    await UpscaleWithFaceRestorationAsync(item, settings, ct);
                    break;
                    
                default:
                    await UpscaleWithFFmpegAsync(item, settings, ct);
                    break;
            }
        }
        
        private async Task UpscaleWithAIAsync(BatchUpscaleItem item, BatchUpscaleSettings settings, CancellationToken ct)
        {
            // Real-ESRGAN command line
            var modelName = settings.Mode switch
            {
                UpscaleMode.ESRGAN => "ESRGAN",
                UpscaleMode.RealESRGAN => "realesrgan-x4plus",
                UpscaleMode.BSRGAN => "BSRGANx4",
                UpscaleMode.SwinIR => "SwinIR",
                _ => "realesrgan-x4plus"
            };
            
            var args = $"-i \"{item.SourcePath}\" -o \"{item.OutputPath}\" -n {modelName} -s {settings.ScaleFactor}";
            
            if (settings.UseGpu)
            {
                args += $" -g {settings.GpuId}";
            }
            else
            {
                args += " -g -1"; // CPU mode
            }
            
            var psi = new ProcessStartInfo
            {
                FileName = "realesrgan-ncnn-vulkan",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new Exception("Failed to start Real-ESRGAN process");
            }
            
            // Monitor progress (Real-ESRGAN outputs progress to stderr)
            var lastProgress = 0.0;
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null && e.Data.Contains("%"))
                {
                    if (double.TryParse(e.Data.Replace("%", "").Trim(), out var progress))
                    {
                        item.Progress = progress;
                        lastProgress = progress;
                    }
                }
            };
            process.BeginErrorReadLine();
            
            await process.WaitForExitAsync(ct);
            
            if (process.ExitCode != 0)
            {
                throw new Exception($"Real-ESRGAN failed with exit code {process.ExitCode}");
            }
        }
        
        private async Task UpscaleWithFaceRestorationAsync(BatchUpscaleItem item, BatchUpscaleSettings settings, CancellationToken ct)
        {
            // GFPGAN/CodeFormer for face restoration
            var tool = settings.Mode == UpscaleMode.GFPGAN ? "gfpgan" : "codeformer";
            
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"-m {tool} -i \"{item.SourcePath}\" -o \"{item.OutputPath}\" -s {settings.ScaleFactor}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(ct);
                item.Progress = 100;
            }
        }
        
        private async Task UpscaleWithFFmpegAsync(BatchUpscaleItem item, BatchUpscaleSettings settings, CancellationToken ct)
        {
            // FFmpeg-based upscaling for traditional methods
            var scaleFilter = settings.Mode switch
            {
                UpscaleMode.Lanczos => $"scale={item.OutputWidth}:{item.OutputHeight}:flags=lanczos",
                UpscaleMode.Bicubic => $"scale={item.OutputWidth}:{item.OutputHeight}:flags=bicubic",
                UpscaleMode.Bilinear => $"scale={item.OutputWidth}:{item.OutputHeight}:flags=bilinear",
                UpscaleMode.NearestNeighbor => $"scale={item.OutputWidth}:{item.OutputHeight}:flags=neighbor",
                _ => $"scale={item.OutputWidth}:{item.OutputHeight}:flags=lanczos"
            };
            
            var outputArgs = settings.OutputFormat.ToLower() switch
            {
                "jpg" or "jpeg" => $"-q:v {(int)(31 - (settings.JpegQuality / 100.0 * 31))}",
                "png" => $"-compression_level {settings.PngCompression}",
                "webp" => "-quality 95",
                _ => ""
            };
            
            var args = $"-y -i \"{item.SourcePath}\" -vf \"{scaleFilter}\" {outputArgs} \"{item.OutputPath}\"";
            
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(ct);
                item.Progress = 100;
                
                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    throw new Exception($"FFmpeg failed: {error}");
                }
            }
        }
        
        private void UpdateJobProgress(BatchUpscaleJob job)
        {
            var total = job.Items.Count;
            var completed = job.Items.Count(i => 
                i.Status == BatchJobStatus.Completed || 
                i.Status == BatchJobStatus.Failed || 
                i.Status == BatchJobStatus.Cancelled);
            var itemProgress = job.Items.Sum(i => i.Progress);
            
            job.OverallProgress = total > 0 ? itemProgress / total : 0;
            OverallProgressChanged?.Invoke(this, job.OverallProgress);
        }
        
        private string GetOutputPath(string sourcePath, BatchUpscaleSettings settings)
        {
            var fileName = settings.GetOutputFileName(sourcePath);
            
            if (!string.IsNullOrEmpty(settings.OutputDirectory))
            {
                return Path.Combine(settings.OutputDirectory, fileName);
            }
            
            return Path.Combine(Path.GetDirectoryName(sourcePath) ?? "", fileName);
        }
    }
}
