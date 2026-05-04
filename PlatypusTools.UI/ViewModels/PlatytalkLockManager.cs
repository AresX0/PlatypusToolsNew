using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Local PIN-lock + idle-timeout for the Platytalk view.
    ///
    /// - PIN is hashed with PBKDF2-SHA256 (200k iterations, 16-byte salt) and
    ///   stored under %LOCALAPPDATA%\PlatypusTools\platytalk-lock.json.
    /// - Default idle timeout is 15 minutes; user-configurable. 0 = never.
    /// - Locks immediately on application deactivation (Alt-Tab / lock screen).
    /// - The plaintext PIN is held only for the duration of a verify/set call.
    ///
    /// This sits on the WPF side because it owns a DispatcherTimer and hooks
    /// Application.Current.Deactivated. The PlatytalkViewModel exposes it as
    /// a child VM via the `Lock` property.
    /// </summary>
    public sealed class PlatytalkLockManager : BindableBase
    {
        private const int Pbkdf2Iterations = 200_000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools");
        private static readonly string ConfigFile = Path.Combine(DataDir, "platytalk-lock.json");

        private LockConfig _config = new();
        private readonly DispatcherTimer _idleTimer;
        private bool _hookedAppEvents;

        public PlatytalkLockManager()
        {
            try { Directory.CreateDirectory(DataDir); } catch { /* best effort */ }
            Load();

            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            _idleTimer.Tick += (_, _) => Lock();

            LockNowCommand = new RelayCommand(_ => Lock(), _ => IsPinSet);
            SubmitPinCommand = new RelayCommand(_ => SubmitPin());
            ResetPinCommand = new RelayCommand(_ => ResetPin());

            HookAppEvents();
        }

        // ---------- Public state ------------------------------------------

        public bool IsPinSet => !string.IsNullOrEmpty(_config.HashB64) && !string.IsNullOrEmpty(_config.SaltB64);

        private bool _isLocked;
        public bool IsLocked
        {
            get => _isLocked;
            private set
            {
                if (_isLocked == value) return;
                _isLocked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LockBannerVisible));
            }
        }

        // Visible whenever locked OR when first-time PIN setup is required.
        public bool LockBannerVisible => IsLocked || (Stage != LockStage.Idle);

        public enum LockStage { Idle, Unlock, SetPin, ConfirmPin }

        private LockStage _stage = LockStage.Idle;
        public LockStage Stage
        {
            get => _stage;
            private set { _stage = value; OnPropertyChanged(); OnPropertyChanged(nameof(LockBannerVisible)); OnPropertyChanged(nameof(StageHeading)); OnPropertyChanged(nameof(StageSubtitle)); OnPropertyChanged(nameof(SubmitButtonLabel)); OnPropertyChanged(nameof(IsConfirmStage)); OnPropertyChanged(nameof(IsResetVisible)); }
        }

        public string StageHeading => Stage switch
        {
            LockStage.SetPin => "🔒 SET PIN",
            LockStage.ConfirmPin => "🔒 CONFIRM PIN",
            _ => "🔒 LOCKED",
        };

        public string StageSubtitle => Stage switch
        {
            LockStage.SetPin => "Set a PIN (4–32 chars) to lock Platytalk on this device. You'll be asked for it when the app is idle.",
            LockStage.ConfirmPin => "Re-enter the PIN to confirm.",
            _ => "Enter your PIN to unlock Platytalk.",
        };

        public string SubmitButtonLabel => Stage switch
        {
            LockStage.SetPin => "CONTINUE",
            LockStage.ConfirmPin => "SAVE PIN",
            _ => "UNLOCK",
        };

        public bool IsConfirmStage => Stage == LockStage.ConfirmPin;
        public bool IsResetVisible => Stage == LockStage.Unlock && IsPinSet;

        // CurrentPin is pushed in by the View's PasswordChanged event so the plaintext
        // never sits on a bound public DependencyProperty. The view shows a single
        // PasswordBox and clears it on stage transitions.
        private string _currentPin = string.Empty;
        public string CurrentPin { get => _currentPin; set { _currentPin = value ?? string.Empty; OnPropertyChanged(); } }

        // Held server-side (in-memory only) between SetPin and ConfirmPin stages.
        private string _firstPinDuringSetup = string.Empty;

        private string _pinError = string.Empty;
        public string PinError { get => _pinError; set { _pinError = value ?? string.Empty; OnPropertyChanged(); } }

        public int TimeoutMinutes
        {
            get => _config.TimeoutMinutes;
            set
            {
                if (value < 0) value = 0;
                if (_config.TimeoutMinutes == value) return;
                _config.TimeoutMinutes = value;
                Save();
                ResetIdle();
                OnPropertyChanged();
            }
        }

        public ICommand LockNowCommand { get; }
        public ICommand SubmitPinCommand { get; }
        public ICommand ResetPinCommand { get; }

        // ---------- Lifecycle ---------------------------------------------

        /// <summary>Called by PlatytalkViewModel after sign-in completes.</summary>
        public void OnSignedIn()
        {
            if (IsPinSet)
            {
                Stage = LockStage.Unlock;
                IsLocked = true;
            }
            else
            {
                Stage = LockStage.SetPin;
                IsLocked = true; // require PIN setup before showing chat
            }
        }

        /// <summary>Called when the user signs out of Platytalk.</summary>
        public void OnSignedOut()
        {
            IsLocked = false;
            Stage = LockStage.Idle;
            CurrentPin = string.Empty;
            _firstPinDuringSetup = string.Empty;
            PinError = string.Empty;
            _idleTimer.Stop();
        }

        /// <summary>Resets the idle timer; call from any keyboard/mouse activity.</summary>
        public void ResetIdle()
        {
            if (IsLocked) return;
            if (!IsPinSet) return;
            _idleTimer.Stop();
            if (_config.TimeoutMinutes <= 0) return; // 0 = never until app exit
            _idleTimer.Interval = TimeSpan.FromMinutes(_config.TimeoutMinutes);
            _idleTimer.Start();
        }

        public void Lock()
        {
            if (!IsPinSet) return;
            _idleTimer.Stop();
            CurrentPin = string.Empty;
            _firstPinDuringSetup = string.Empty;
            PinError = string.Empty;
            Stage = LockStage.Unlock;
            IsLocked = true;
        }

        // ---------- PIN flow ----------------------------------------------

        private void SubmitPin()
        {
            PinError = string.Empty;
            switch (Stage)
            {
                case LockStage.Unlock:
                    if (string.IsNullOrEmpty(CurrentPin)) { PinError = "Enter your PIN."; return; }
                    if (!Verify(CurrentPin))
                    {
                        PinError = "Wrong PIN.";
                        CurrentPin = string.Empty;
                        return;
                    }
                    CurrentPin = string.Empty;
                    IsLocked = false;
                    Stage = LockStage.Idle;
                    ResetIdle();
                    break;

                case LockStage.SetPin:
                    if ((CurrentPin?.Length ?? 0) < 4) { PinError = "PIN must be at least 4 characters."; return; }
                    _firstPinDuringSetup = CurrentPin!;
                    CurrentPin = string.Empty;
                    Stage = LockStage.ConfirmPin;
                    break;

                case LockStage.ConfirmPin:
                    if (CurrentPin != _firstPinDuringSetup)
                    {
                        PinError = "PINs don't match.";
                        CurrentPin = string.Empty;
                        _firstPinDuringSetup = string.Empty;
                        Stage = LockStage.SetPin;
                        return;
                    }
                    SetPin(CurrentPin!);
                    CurrentPin = string.Empty;
                    _firstPinDuringSetup = string.Empty;
                    IsLocked = false;
                    Stage = LockStage.Idle;
                    ResetIdle();
                    break;
            }
        }

        private void ResetPin()
        {
            var r = MessageBox.Show(
                "Reset PIN? This clears your PIN; you'll be asked to set a new one. Your messages and contacts are kept.",
                "Reset PIN", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (r != MessageBoxResult.OK) return;
            try { if (File.Exists(ConfigFile)) File.Delete(ConfigFile); } catch { }
            _config = new LockConfig { TimeoutMinutes = _config.TimeoutMinutes };
            OnPropertyChanged(nameof(IsPinSet));
            OnPropertyChanged(nameof(IsResetVisible));
            CurrentPin = string.Empty;
            _firstPinDuringSetup = string.Empty;
            PinError = string.Empty;
            Stage = LockStage.SetPin;
            IsLocked = true;
        }

        // ---------- Crypto -------------------------------------------------

        private void SetPin(string pin)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = Pbkdf2(pin, salt);
            _config.SaltB64 = Convert.ToBase64String(salt);
            _config.HashB64 = Convert.ToBase64String(hash);
            _config.Iterations = Pbkdf2Iterations;
            Save();
            OnPropertyChanged(nameof(IsPinSet));
            OnPropertyChanged(nameof(IsResetVisible));
        }

        private bool Verify(string pin)
        {
            if (!IsPinSet) return false;
            try
            {
                var salt = Convert.FromBase64String(_config.SaltB64!);
                var stored = Convert.FromBase64String(_config.HashB64!);
                var iter = _config.Iterations > 0 ? _config.Iterations : Pbkdf2Iterations;
                var actual = Pbkdf2(pin, salt, iter);
                return CryptographicOperations.FixedTimeEquals(actual, stored);
            }
            catch { return false; }
        }

        private static byte[] Pbkdf2(string pin, byte[] salt, int iterations = Pbkdf2Iterations)
        {
            using var k = new Rfc2898DeriveBytes(pin, salt, iterations, HashAlgorithmName.SHA256);
            return k.GetBytes(HashBytes);
        }

        // ---------- Persistence -------------------------------------------

        private void Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    var c = JsonSerializer.Deserialize<LockConfig>(json);
                    if (c != null) _config = c;
                }
            }
            catch { _config = new LockConfig(); }
            if (_config.TimeoutMinutes < 0) _config.TimeoutMinutes = 15;
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(ConfigFile, JsonSerializer.Serialize(_config));
            }
            catch { /* best effort */ }
        }

        // ---------- App-level hooks ---------------------------------------

        private void HookAppEvents()
        {
            if (_hookedAppEvents) return;
            var app = Application.Current;
            if (app == null) return;
            app.Deactivated += (_, _) => { if (IsPinSet && !IsLocked) Lock(); };
            _hookedAppEvents = true;
        }

        // ---------- Config record -----------------------------------------

        private sealed class LockConfig
        {
            public string? SaltB64 { get; set; }
            public string? HashB64 { get; set; }
            public int Iterations { get; set; } = Pbkdf2Iterations;
            public int TimeoutMinutes { get; set; } = 15;
        }
    }
}
