using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

[assembly: AssemblyTitle("Velvet Vault")]
[assembly: AssemblyDescription("Local encrypted password manager")]
[assembly: AssemblyCompany("Velvet Vault")]
[assembly: AssemblyProduct("Velvet Vault")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

namespace VelvetVault
{
    public class PasswordHistoryItem
    {
        public string Password { get; set; }
        public DateTime ChangedAt { get; set; }

        [ScriptIgnore]
        public string Label
        {
            get
            {
                string mask = string.IsNullOrEmpty(Password) ? "пустой пароль" : new string('•', Math.Min(Password.Length, 16));
                return ChangedAt.ToLocalTime().ToString("dd.MM.yyyy  HH:mm") + "   " + mask;
            }
        }
    }

    public class VaultEntry : INotifyPropertyChanged
    {
        private string _title;
        private string _username;
        private string _password;
        private string _website;
        private string _notes;
        private bool _favorite;
        private DateTime _updatedAt;

        public VaultEntry() { PasswordHistory = new List<PasswordHistoryItem>(); }

        public string Id { get; set; }
        public string Title { get { return _title; } set { _title = value; Changed("Title"); Changed("Initial"); } }
        public string Username { get { return _username; } set { _username = value; Changed("Username"); } }
        public string Password { get { return _password; } set { _password = value; Changed("Password"); Changed("MaskedPassword"); } }
        public string Website { get { return _website; } set { _website = value; Changed("Website"); Changed("Subtitle"); } }
        public string Notes { get { return _notes; } set { _notes = value; Changed("Notes"); } }
        public bool Favorite { get { return _favorite; } set { _favorite = value; Changed("Favorite"); Changed("FavoriteMark"); } }
        public DateTime UpdatedAt { get { return _updatedAt; } set { _updatedAt = value; Changed("UpdatedAt"); Changed("UpdatedLabel"); } }
        public List<PasswordHistoryItem> PasswordHistory { get; set; }

        public string Initial { get { return string.IsNullOrWhiteSpace(Title) ? "?" : Title.Trim().Substring(0, 1).ToUpperInvariant(); } }
        public string Subtitle { get { return !string.IsNullOrWhiteSpace(Username) ? Username : (!string.IsNullOrWhiteSpace(Website) ? Website : "Без логина"); } }
        public string MaskedPassword { get { return string.IsNullOrEmpty(Password) ? "Пароль не задан" : new string('•', Math.Min(Password.Length, 14)); } }
        public string FavoriteMark { get { return Favorite ? "★" : ""; } }
        public string UpdatedLabel { get { return UpdatedAt == default(DateTime) ? "" : "Изменено " + UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string name) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name)); }
    }

    public class VaultPayload
    {
        public List<VaultEntry> Entries { get; set; }
    }

    public class MineGameSave
    {
        public int Coins { get; set; }
        public int Ore { get; set; }
        public int BackpackCapacity { get; set; }
        public int PickaxeLevel { get; set; }
        public int TotalMined { get; set; }
        public int TotalSold { get; set; }
        public int Steps { get; set; }
    }

    public class NoteItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class TaskItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public bool IsDone { get; set; }
        [ScriptIgnore]
        public string DisplayTitle { get { return (IsDone ? "✓  " : "○  ") + Title; } }
    }

    public class FinanceItem
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public double Amount { get; set; }
        public DateTime CreatedAt { get; set; }
        [ScriptIgnore]
        public string DisplayText { get { return (Amount >= 0 ? "+ " : "− ") + Math.Abs(Amount).ToString("0.00", CultureInfo.InvariantCulture) + " ₴  ·  " + Description; } }
    }

    public class DocumentItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class HubData
    {
        public List<NoteItem> Notes { get; set; }
        public List<TaskItem> Tasks { get; set; }
        public List<FinanceItem> Finance { get; set; }
        public List<DocumentItem> Documents { get; set; }
    }

    public class VaultOpenResult
    {
        public List<VaultEntry> Entries { get; set; }
        public byte[] Salt { get; set; }
        public byte[] KeyMaterial { get; set; }
    }

    public static class VaultCrypto
    {
        private static readonly byte[] MagicV1 = Encoding.ASCII.GetBytes("VV01");
        private static readonly byte[] MagicV2 = Encoding.ASCII.GetBytes("VV02");
        private const int Iterations = 310000;

        public static void Save(string path, string masterPassword, IList<VaultEntry> entries)
        {
            byte[] salt = RandomBytes(16);
            byte[] keyMaterial = DeriveKey(masterPassword, salt);
            SaveWithKey(path, salt, keyMaterial, entries);
            Array.Clear(keyMaterial, 0, keyMaterial.Length);
        }

        public static List<VaultEntry> Load(string path, string masterPassword)
        {
            VaultOpenResult result = Open(path, masterPassword);
            List<VaultEntry> loaded = result.Entries;
            Array.Clear(result.KeyMaterial, 0, result.KeyMaterial.Length);
            return loaded;
        }

        public static VaultOpenResult CreateSession(string masterPassword)
        {
            byte[] salt = RandomBytes(16);
            return new VaultOpenResult { Entries = new List<VaultEntry>(), Salt = salt, KeyMaterial = DeriveKey(masterPassword, salt) };
        }

        public static VaultOpenResult Open(string path, string masterPassword)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 4 + 16 + 16 + 4 + 32) throw new InvalidDataException("Файл хранилища повреждён.");
            byte[] magic = data.Take(4).ToArray();
            if (!magic.SequenceEqual(MagicV1) && !magic.SequenceEqual(MagicV2)) throw new InvalidDataException("Неизвестный формат хранилища.");

            byte[] salt = data.Skip(4).Take(16).ToArray();
            byte[] iv = data.Skip(20).Take(16).ToArray();
            int cipherLength = BitConverter.ToInt32(data, 36);
            if (cipherLength < 16 || cipherLength > data.Length - 72) throw new InvalidDataException("Файл хранилища повреждён.");
            byte[] cipher = data.Skip(40).Take(cipherLength).ToArray();
            byte[] actualMac = data.Skip(40 + cipherLength).Take(32).ToArray();
            byte[] authenticated = data.Take(40 + cipherLength).ToArray();

            byte[] keyMaterial = DeriveKey(masterPassword, salt);
            byte[] expectedMac;
            using (HMACSHA256 hmac = new HMACSHA256(keyMaterial.Skip(32).Take(32).ToArray()))
                expectedMac = hmac.ComputeHash(authenticated);
            if (!FixedTimeEquals(actualMac, expectedMac))
            {
                Array.Clear(keyMaterial, 0, keyMaterial.Length);
                throw new CryptographicException("Неверный мастер-пароль или файл был изменён.");
            }

            byte[] plain;
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = keyMaterial.Take(32).ToArray();
                aes.IV = iv;
                using (ICryptoTransform transform = aes.CreateDecryptor())
                    plain = transform.TransformFinalBlock(cipher, 0, cipher.Length);
            }
            string json = Encoding.UTF8.GetString(plain);
            VaultPayload payload = new JavaScriptSerializer().Deserialize<VaultPayload>(json);
            Array.Clear(plain, 0, plain.Length);
            return new VaultOpenResult { Entries = payload != null && payload.Entries != null ? payload.Entries : new List<VaultEntry>(), Salt = salt, KeyMaterial = keyMaterial };
        }

        public static void SaveWithKey(string path, byte[] salt, byte[] keyMaterial, IList<VaultEntry> entries)
        {
            if (salt == null || salt.Length != 16 || keyMaterial == null || keyMaterial.Length != 64) throw new CryptographicException("Сеанс шифрования недоступен.");
            byte[] iv = RandomBytes(16);
            byte[] plain = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new VaultPayload { Entries = entries.ToList() }));
            byte[] cipher;
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = keyMaterial.Take(32).ToArray();
                aes.IV = iv;
                using (ICryptoTransform transform = aes.CreateEncryptor())
                    cipher = transform.TransformFinalBlock(plain, 0, plain.Length);
            }
            byte[] authenticated = Combine(MagicV2, salt, iv, BitConverter.GetBytes(cipher.Length), cipher);
            byte[] mac;
            using (HMACSHA256 hmac = new HMACSHA256(keyMaterial.Skip(32).Take(32).ToArray()))
                mac = hmac.ComputeHash(authenticated);
            WriteAtomically(path, Combine(authenticated, mac));
            Array.Clear(plain, 0, plain.Length);
        }

        private static byte[] DeriveKey(string masterPassword, byte[] salt)
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(masterPassword);
            byte[] nativeKey = new byte[64];
            IntPtr algorithm = IntPtr.Zero;
            try
            {
                int status = BCryptOpenAlgorithmProvider(out algorithm, "SHA256", null, 0x00000008);
                if (status == 0)
                {
                    status = BCryptDeriveKeyPBKDF2(algorithm, passwordBytes, passwordBytes.Length, salt, salt.Length, (ulong)Iterations, nativeKey, nativeKey.Length, 0);
                    if (status == 0) return nativeKey;
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            finally
            {
                if (algorithm != IntPtr.Zero) BCryptCloseAlgorithmProvider(algorithm, 0);
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
            Array.Clear(nativeKey, 0, nativeKey.Length);
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(masterPassword, salt, Iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(64);
        }

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm, string algorithmId, string implementation, uint flags);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptDeriveKeyPBKDF2(IntPtr algorithm, byte[] password, int passwordLength, byte[] salt, int saltLength, ulong iterations, byte[] derivedKey, int derivedKeyLength, uint flags);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm, uint flags);

        private static void WriteAtomically(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            if (File.Exists(path))
            {
                string backup = path + ".bak";
                try { File.Replace(temp, path, backup, true); }
                catch { File.Delete(path); File.Move(temp, path); }
            }
            else File.Move(temp, path);
        }

        private static byte[] RandomBytes(int count)
        {
            byte[] bytes = new byte[count];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static byte[] Combine(params byte[][] arrays)
        {
            byte[] result = new byte[arrays.Sum(x => x.Length)];
            int offset = 0;
            foreach (byte[] array in arrays) { Buffer.BlockCopy(array, 0, result, offset, array.Length); offset += array.Length; }
            return result;
        }
    }

    public class MainWindow : Window
    {
        private const string AppName = "Velvet Vault";
        private readonly string vaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VelvetVault", "vault.dat");
        private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VelvetVault", "theme.txt");
        private readonly string mineGamePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VelvetVault", "mine-game.json");
        private readonly string hubDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VelvetVault", "hub-data.json");
        private readonly ObservableCollection<VaultEntry> entries = new ObservableCollection<VaultEntry>();
        private readonly ObservableCollection<NoteItem> hubNotes = new ObservableCollection<NoteItem>();
        private readonly ObservableCollection<TaskItem> hubTasks = new ObservableCollection<TaskItem>();
        private readonly ObservableCollection<FinanceItem> hubFinance = new ObservableCollection<FinanceItem>();
        private readonly ObservableCollection<DocumentItem> hubDocuments = new ObservableCollection<DocumentItem>();
        private ICollectionView entriesView;
        private byte[] sessionSalt;
        private byte[] sessionKey;
        private DateTime lastActivity = DateTime.Now;
        private DispatcherTimer idleTimer;

        private Grid loginView;
        private Grid mainView;
        private TextBlock loginTitle;
        private TextBlock loginSubtitle;
        private PasswordBox loginPassword;
        private PasswordBox confirmPassword;
        private FrameworkElement confirmPanel;
        private Button loginButton;
        private TextBlock loginError;
        private Grid loginProgress;
        private Border loginProgressGlow;
        private TextBox searchBox;
        private ListBox entriesList;
        private TextBlock totalCount;
        private TextBlock favoriteCount;
        private TextBlock emptyText;
        private Grid detailEmpty;
        private ScrollViewer detailEditor;
        private TextBox titleBox;
        private TextBox usernameBox;
        private PasswordBox passwordBox;
        private TextBox visiblePasswordBox;
        private CheckBox showPassword;
        private TextBox websiteBox;
        private TextBox notesBox;
        private CheckBox favoriteBox;
        private Slider lengthSlider;
        private TextBlock lengthLabel;
        private CheckBox lowerCheck;
        private CheckBox upperCheck;
        private CheckBox digitsCheck;
        private CheckBox symbolsCheck;
        private TextBlock strengthText;
        private Border strengthBar;
        private Border historyPanel;
        private ComboBox passwordHistoryBox;
        private TextBlock historyCount;
        private TextBlock updatedLabel;
        private ComboBox themeSelector;
        private TextBlock healthScore;
        private TextBlock strongCount;
        private TextBlock mediumCount;
        private TextBlock weakCount;
        private Border strongBar;
        private Border mediumBar;
        private Border weakBar;
        private Canvas ambientCanvas;
        private FrameworkElement sidebarPanel;
        private FrameworkElement workspacePanel;
        private Grid gameView;
        private Canvas gameCanvas;
        private TextBlock gameCoinsText;
        private TextBlock gameOreText;
        private TextBlock gamePickaxeText;
        private TextBlock gameBackpackText;
        private TextBlock gameStatusText;
        private Grid notesView;
        private Grid tasksView;
        private Grid financeView;
        private Grid converterView;
        private Grid documentsView;
        private Grid calculatorView;
        private ListBox notesList;
        private TextBox noteTitleBox;
        private TextBox noteTextBox;
        private ListBox tasksList;
        private TextBox taskInput;
        private ListBox financeList;
        private TextBox financeDescriptionBox;
        private TextBox financeAmountBox;
        private ComboBox financeTypeBox;
        private TextBlock financeBalanceText;
        private TextBlock converterSourceText;
        private ComboBox converterFormatBox;
        private ListBox documentsList;
        private TextBox documentTitleBox;
        private TextBox documentContentBox;
        private TextBox calculatorDisplay;
        private NoteItem selectedNote;
        private DocumentItem selectedDocument;
        private string converterSourcePath;
        private double calculatorStored;
        private string calculatorOperation;
        private bool calculatorNewNumber = true;
        private bool updatingNoteEditor;
        private Border toast;
        private TextBlock toastText;
        private Grid dialogOverlay;
        private Border dialogCard;
        private TextBlock dialogTitle;
        private TextBlock dialogMessage;
        private Button dialogCancelButton;
        private Button dialogConfirmButton;
        private Action dialogAction;
        private VaultEntry selectedEntry;
        private Window parsedWindow;
        private bool ambientStarted;
        private readonly Random gameRandom = new Random();
        private readonly HashSet<string> gameOreCells = new HashSet<string>();
        private int gamePlayerX = 1;
        private int gamePlayerY = 1;
        private int gameCoins;
        private int gameOre;
        private int gameBackpackCapacity = 20;
        private int gamePickaxeLevel = 1;
        private int gameTotalMined;
        private int gameTotalSold;
        private int gameSteps;
        private const int GameColumns = 14;
        private const int GameRows = 8;
        private const int GameTileSize = 64;

        public MainWindow()
        {
            InitializeUi();
            WireEvents();
            LoadTheme();
            InitializeMineGame();
            LoadHubData();
            ShowLogin();
            idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            idleTimer.Tick += delegate { if (mainView.Visibility == Visibility.Visible && DateTime.Now - lastActivity > TimeSpan.FromMinutes(10)) LockVault("Хранилище заблокировано после 10 минут бездействия"); };
            idleTimer.Start();
        }

        private void InitializeUi()
        {
            string xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Title='Velvet Vault' Width='1440' Height='900' MinWidth='1020' MinHeight='680' WindowStyle='None' AllowsTransparency='True' Background='Transparent' ResizeMode='CanResizeWithGrip' FontFamily='Segoe UI' Opacity='0'>
  <Window.Resources>
    <LinearGradientBrush x:Key='AccentBrush' StartPoint='0,0' EndPoint='1,1'><GradientStop Color='#8B5CF6' Offset='0'/><GradientStop Color='#EC4899' Offset='1'/></LinearGradientBrush>
    <SolidColorBrush x:Key='Ink' Color='#18192A'/><SolidColorBrush x:Key='Muted' Color='#74778A'/><SolidColorBrush x:Key='Line' Color='#E8E9F1'/>
    <SolidColorBrush x:Key='CanvasBrush' Color='#F4F5FA'/><SolidColorBrush x:Key='SurfaceBrush' Color='#FFFFFF'/><SolidColorBrush x:Key='TitleBrush' Color='#F9F9FC'/><SolidColorBrush x:Key='SidebarBrush' Color='#191A2D'/><SolidColorBrush x:Key='FieldBrush' Color='#F7F7FB'/><SolidColorBrush x:Key='FieldBorderBrush' Color='#E7E7F0'/><SolidColorBrush x:Key='HoverBrush' Color='#F6F4FF'/><SolidColorBrush x:Key='SelectedBrush' Color='#EEEAFE'/><SolidColorBrush x:Key='SoftAccentBrush' Color='#F0EDFF'/><SolidColorBrush x:Key='SoftPanelBrush' Color='#F7F5FF'/><SolidColorBrush x:Key='ScrollThumbBrush' Color='#708B5CF6'/><SolidColorBrush x:Key='ScrollThumbHoverBrush' Color='#D08B5CF6'/><SolidColorBrush x:Key='GhostBrush' Color='#F0EEFF'/><SolidColorBrush x:Key='GhostTextBrush' Color='#4F46E5'/><SolidColorBrush x:Key='DangerBrush' Color='#FFF0F3'/><SolidColorBrush x:Key='DangerTextBrush' Color='#DC365B'/><SolidColorBrush x:Key='IconHoverBrush' Color='#EEEFFA'/>
    <Style x:Key='ActionButton' TargetType='Button'>
      <Setter Property='Foreground' Value='White'/><Setter Property='Background' Value='{DynamicResource AccentBrush}'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='18,11'/><Setter Property='FontSize' Value='14'/><Setter Property='FontWeight' Value='SemiBold'/><Setter Property='Cursor' Value='Hand'/><Setter Property='RenderTransformOrigin' Value='.5,.5'/><Setter Property='RenderTransform'><Setter.Value><ScaleTransform ScaleX='1' ScaleY='1'/></Setter.Value></Setter>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='12' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='.45'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='.92'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Opacity' Value='.78'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
      <Style.Triggers><EventTrigger RoutedEvent='MouseEnter'><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetProperty='RenderTransform.ScaleX' To='1.025' Duration='0:0:.13'/><DoubleAnimation Storyboard.TargetProperty='RenderTransform.ScaleY' To='1.025' Duration='0:0:.13'/></Storyboard></BeginStoryboard></EventTrigger><EventTrigger RoutedEvent='MouseLeave'><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetProperty='RenderTransform.ScaleX' To='1' Duration='0:0:.13'/><DoubleAnimation Storyboard.TargetProperty='RenderTransform.ScaleY' To='1' Duration='0:0:.13'/></Storyboard></BeginStoryboard></EventTrigger></Style.Triggers>
    </Style>
    <Style x:Key='GhostButton' TargetType='Button' BasedOn='{StaticResource ActionButton}'><Setter Property='Foreground' Value='{DynamicResource GhostTextBrush}'/><Setter Property='Background' Value='{DynamicResource GhostBrush}'/><Setter Property='Padding' Value='14,10'/></Style>
    <Style x:Key='DangerButton' TargetType='Button' BasedOn='{StaticResource ActionButton}'><Setter Property='Foreground' Value='{DynamicResource DangerTextBrush}'/><Setter Property='Background' Value='{DynamicResource DangerBrush}'/></Style>
    <Style x:Key='AppNavButton' TargetType='Button'>
      <Setter Property='Foreground' Value='#AEB1C1'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='13,10'/><Setter Property='Margin' Value='0,0,0,6'/><Setter Property='FontSize' Value='12'/><Setter Property='FontWeight' Value='SemiBold'/><Setter Property='HorizontalContentAlignment' Value='Left'/><Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='NavCard' Background='{TemplateBinding Background}' CornerRadius='11' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='NavCard' Property='Background' Value='#25273D'/><Setter Property='Foreground' Value='White'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='NavCard' Property='Opacity' Value='.75'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key='AppNavActive' TargetType='Button' BasedOn='{StaticResource AppNavButton}'><Setter Property='Foreground' Value='White'/><Setter Property='Background' Value='#2C2D46'/></Style>
    <Style x:Key='Field' TargetType='TextBox'><Setter Property='Background' Value='{DynamicResource FieldBrush}'/><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='BorderBrush' Value='{DynamicResource FieldBorderBrush}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='12,10'/><Setter Property='FontSize' Value='14'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='CaretBrush' Value='{DynamicResource Ink}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'><Border x:Name='FieldBorder' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10'><ScrollViewer x:Name='PART_ContentHost'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='FieldBorder' Property='BorderBrush' Value='{DynamicResource GhostTextBrush}'/><Setter TargetName='FieldBorder' Property='BorderThickness' Value='1.5'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='FieldBorder' Property='Opacity' Value='.55'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style x:Key='PasswordField' TargetType='PasswordBox'><Setter Property='Background' Value='{DynamicResource FieldBrush}'/><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='BorderBrush' Value='{DynamicResource FieldBorderBrush}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='12,10'/><Setter Property='FontFamily' Value='Segoe UI Symbol'/><Setter Property='FontSize' Value='15'/><Setter Property='FontWeight' Value='SemiBold'/><Setter Property='PasswordChar' Value='◆'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='CaretBrush' Value='{DynamicResource Ink}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='PasswordBox'><Border x:Name='FieldBorder' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10'><ScrollViewer x:Name='PART_ContentHost'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='FieldBorder' Property='BorderBrush' Value='{DynamicResource GhostTextBrush}'/><Setter TargetName='FieldBorder' Property='BorderThickness' Value='1.5'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='FieldBorder' Property='Opacity' Value='.55'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style x:Key='Label' TargetType='TextBlock'><Setter Property='Foreground' Value='{DynamicResource Muted}'/><Setter Property='FontSize' Value='12'/><Setter Property='FontWeight' Value='SemiBold'/><Setter Property='Margin' Value='0,0,0,6'/></Style>
    <Style x:Key='IconButton' TargetType='Button'><Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='{DynamicResource Muted}'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='10,6'/><Setter Property='FontSize' Value='16'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource IconHoverBrush}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style TargetType='CheckBox'><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='FontSize' Value='13'/><Setter Property='Margin' Value='0,5'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Border x:Name='Box' Width='16' Height='16' CornerRadius='5' Background='{DynamicResource FieldBrush}' BorderBrush='{DynamicResource FieldBorderBrush}' BorderThickness='1' VerticalAlignment='Center'><TextBlock x:Name='Tick' Text='✓' Foreground='White' FontSize='11' FontWeight='Bold' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/></Border><ContentPresenter Grid.Column='1' Margin='7,0,0,0' VerticalAlignment='Center'/></Grid><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='{DynamicResource AccentBrush}'/><Setter TargetName='Box' Property='BorderBrush' Value='Transparent'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource GhostTextBrush}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style TargetType='ComboBoxItem'><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Padding' Value='10,8'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border x:Name='ItemBorder' Background='Transparent' CornerRadius='8' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='{DynamicResource HoverBrush}'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='{DynamicResource SelectedBrush}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style x:Key='ModernComboBox' TargetType='ComboBox'><Setter Property='Background' Value='{DynamicResource FieldBrush}'/><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='BorderBrush' Value='{DynamicResource FieldBorderBrush}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='11,8'/><Setter Property='FontSize' Value='12'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'><Grid><ToggleButton x:Name='DropDownToggle' Focusable='False' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Foreground='{TemplateBinding Foreground}' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border x:Name='ComboBorder' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10' Padding='{TemplateBinding Padding}'><Grid><ContentPresenter ContentTemplate='{TemplateBinding ContentTemplate}' VerticalAlignment='Center' Margin='0,0,24,0'/><TextBlock Text='⌄' Foreground='{DynamicResource Muted}' FontSize='16' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,-4,0,0'/></Grid></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ComboBorder' Property='BorderBrush' Value='{DynamicResource GhostTextBrush}'/></Trigger><Trigger Property='IsChecked' Value='True'><Setter TargetName='ComboBorder' Property='BorderBrush' Value='{DynamicResource GhostTextBrush}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template></ToggleButton><Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'><Border Background='{DynamicResource SurfaceBrush}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='12' Padding='5' Margin='0,5,0,0' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'><ScrollViewer MaxHeight='260' VerticalScrollBarVisibility='Auto'><ItemsPresenter/></ScrollViewer></Border></Popup></Grid><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter TargetName='DropDownToggle' Property='Opacity' Value='.55'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style TargetType='Slider'><Setter Property='Height' Value='28'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'><Grid Margin='2,0'><Track x:Name='PART_Track' Orientation='Horizontal' IsDirectionReversed='False' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}'><Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='{DynamicResource AccentBrush}' CornerRadius='4' Height='7' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width='27' Height='20' Focusable='False'><Thumb.Template><ControlTemplate TargetType='Thumb'><Grid><Border x:Name='SliderThumbGlow' Background='{DynamicResource AccentBrush}' Opacity='.24' CornerRadius='10' Margin='-3'/><Border x:Name='SliderThumb' Background='{DynamicResource AccentBrush}' BorderBrush='{DynamicResource SurfaceBrush}' BorderThickness='2' CornerRadius='8'><StackPanel Orientation='Horizontal' HorizontalAlignment='Center' VerticalAlignment='Center'><Border Width='2' Height='7' CornerRadius='1' Background='White' Opacity='.82' Margin='0,0,3,0'/><Border Width='2' Height='7' CornerRadius='1' Background='White' Opacity='.82'/></StackPanel></Border></Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='SliderThumbGlow' Property='Opacity' Value='.48'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='SliderThumb' Property='BorderThickness' Value='3'/><Setter TargetName='SliderThumbGlow' Property='Opacity' Value='.60'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='{DynamicResource FieldBorderBrush}' CornerRadius='4' Height='7' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>
    <Style TargetType='ScrollBar'>
      <Setter Property='Width' Value='9'/><Setter Property='Background' Value='Transparent'/><Setter Property='Margin' Value='2,4,2,4'/><Setter Property='Opacity' Value='.72'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Background='Transparent' BorderThickness='0' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>
            <Track.Thumb><Thumb MinHeight='42' Cursor='Hand'><Thumb.Template><ControlTemplate TargetType='Thumb'><Grid><Border x:Name='ThumbBody' Background='{DynamicResource ScrollThumbBrush}' CornerRadius='4' Margin='2,0'/></Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ThumbBody' Property='Background' Value='{DynamicResource ScrollThumbHoverBrush}'/><Setter TargetName='ThumbBody' Property='Margin' Value='1,0'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='ThumbBody' Property='Background' Value='{DynamicResource ScrollThumbHoverBrush}'/><Setter TargetName='ThumbBody' Property='Margin' Value='0,0'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Background='Transparent' BorderThickness='0' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>
          </Track>
        </Grid>
        <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='1'/></Trigger></ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
  </Window.Resources>
  <Border x:Name='ShellBorder' CornerRadius='22' Background='{DynamicResource CanvasBrush}' BorderBrush='{DynamicResource Line}' BorderThickness='1' SnapsToDevicePixels='True'>
    <Grid x:Name='Root'>
      <Grid.RowDefinitions><RowDefinition Height='42'/><RowDefinition Height='*'/></Grid.RowDefinitions>
      <Border x:Name='TitleBar' Grid.Row='0' Background='{DynamicResource TitleBrush}' CornerRadius='22,22,0,0'>
        <Grid><StackPanel Orientation='Horizontal' VerticalAlignment='Center' Margin='16,0'><Border Width='20' Height='20' CornerRadius='7' Background='{DynamicResource AccentBrush}'><TextBlock Text='V' Foreground='White' FontSize='10' FontWeight='Bold' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><TextBlock Text='VELVET · ЦЕНТР ПРИЛОЖЕНИЙ' Foreground='#73758A' FontSize='11' FontWeight='Bold' VerticalAlignment='Center' Margin='8,0,0,0'/></StackPanel><StackPanel Orientation='Horizontal' HorizontalAlignment='Right'><Button x:Name='MinButton' Style='{StaticResource IconButton}' Content='—' Width='46' ToolTip='Свернуть'/><Button x:Name='MaxButton' Style='{StaticResource IconButton}' Content='❐' Width='46' ToolTip='Развернуть или восстановить'/><Button x:Name='CloseButton' Style='{StaticResource IconButton}' Content='×' Width='46' Foreground='#A13A54' ToolTip='Закрыть'/></StackPanel></Grid>
      </Border>
      <Grid x:Name='LoginView' Grid.Row='1'>
        <Grid.Background><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'><GradientStop Color='#17182B' Offset='0'/><GradientStop Color='#282141' Offset='.55'/><GradientStop Color='#3D2146' Offset='1'/></LinearGradientBrush></Grid.Background>
        <Ellipse Width='520' Height='520' Fill='#6048D9' Opacity='.16' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='-220,-270,0,0'/><Ellipse Width='430' Height='430' Fill='#EE4E9B' Opacity='.12' HorizontalAlignment='Right' VerticalAlignment='Bottom' Margin='0,0,-150,-230'/>
        <Border Width='430' Background='{DynamicResource SurfaceBrush}' CornerRadius='24' Padding='40' VerticalAlignment='Center' HorizontalAlignment='Center'>
          <StackPanel>
            <Border Width='64' Height='64' CornerRadius='20' Background='{DynamicResource AccentBrush}' HorizontalAlignment='Left' Margin='0,0,0,24'><TextBlock Text='V' Foreground='White' FontSize='30' FontWeight='Bold' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
            <TextBlock x:Name='LoginTitle' Text='Добро пожаловать' FontSize='28' FontWeight='Bold' Foreground='{DynamicResource Ink}'/>
            <TextBlock x:Name='LoginSubtitle' Text='Введите мастер-пароль' FontSize='14' Foreground='{DynamicResource Muted}' Margin='0,8,0,25' TextWrapping='Wrap'/>
            <TextBlock Style='{StaticResource Label}' Text='МАСТЕР-ПАРОЛЬ'/><PasswordBox x:Name='LoginPassword' Style='{StaticResource PasswordField}' Height='44'/>
            <StackPanel x:Name='ConfirmPanel' Margin='0,15,0,0'><TextBlock Style='{StaticResource Label}' Text='ПОВТОРИТЕ ПАРОЛЬ'/><PasswordBox x:Name='ConfirmPassword' Style='{StaticResource PasswordField}' Height='44'/></StackPanel>
            <TextBlock x:Name='LoginError' Foreground='#D93862' FontSize='12' Margin='2,10,0,0' TextWrapping='Wrap'/>
            <Button x:Name='LoginButton' Style='{StaticResource ActionButton}' Content='Открыть хранилище' Height='48' Margin='0,18,0,0'/>
            <Grid x:Name='LoginProgress' Visibility='Collapsed' Height='4' Margin='0,10,0,0' Background='{DynamicResource SoftAccentBrush}' ClipToBounds='True'><Border x:Name='LoginProgressGlow' Width='110' HorizontalAlignment='Left' Background='{DynamicResource AccentBrush}' CornerRadius='2'><Border.RenderTransform><TranslateTransform X='-110'/></Border.RenderTransform></Border></Grid>
            <TextBlock Text='Данные зашифрованы и остаются только на этом компьютере' Foreground='#989AAD' FontSize='11' TextWrapping='Wrap' TextAlignment='Center' Margin='10,14,10,0'/>
          </StackPanel>
        </Border>
      </Grid>
      <Grid x:Name='MainView' Grid.Row='1' Visibility='Collapsed' Background='{DynamicResource CanvasBrush}'>
        <Grid.ColumnDefinitions><ColumnDefinition Width='250'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
        <Canvas x:Name='AmbientCanvas' Grid.ColumnSpan='2' IsHitTestVisible='False' ClipToBounds='True' Opacity='.5'>
          <Ellipse x:Name='OrbOne' Width='300' Height='300' Canvas.Left='160' Canvas.Top='-140' CacheMode='BitmapCache'><Ellipse.Fill><RadialGradientBrush><GradientStop Color='#3C8B5CF6' Offset='0'/><GradientStop Color='#188B5CF6' Offset='.55'/><GradientStop Color='#008B5CF6' Offset='1'/></RadialGradientBrush></Ellipse.Fill><Ellipse.RenderTransform><TranslateTransform/></Ellipse.RenderTransform></Ellipse>
          <Ellipse x:Name='OrbTwo' Width='260' Height='260' Canvas.Right='-70' Canvas.Bottom='-100' CacheMode='BitmapCache'><Ellipse.Fill><RadialGradientBrush><GradientStop Color='#38EC4899' Offset='0'/><GradientStop Color='#16EC4899' Offset='.55'/><GradientStop Color='#00EC4899' Offset='1'/></RadialGradientBrush></Ellipse.Fill><Ellipse.RenderTransform><TranslateTransform/></Ellipse.RenderTransform></Ellipse>
          <Ellipse x:Name='OrbThree' Width='190' Height='190' Canvas.Left='510' Canvas.Bottom='-100' CacheMode='BitmapCache'><Ellipse.Fill><RadialGradientBrush><GradientStop Color='#3522CFA5' Offset='0'/><GradientStop Color='#1422CFA5' Offset='.55'/><GradientStop Color='#0022CFA5' Offset='1'/></RadialGradientBrush></Ellipse.Fill><Ellipse.RenderTransform><TranslateTransform/></Ellipse.RenderTransform></Ellipse>
        </Canvas>
        <Border x:Name='SidebarPanel' Grid.Column='0' Background='{DynamicResource SidebarBrush}' CornerRadius='0,0,0,22'>
          <Grid Margin='18'><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
            <StackPanel Orientation='Horizontal' Margin='2,14,0,24'><Border Width='42' Height='42' CornerRadius='13' Background='{DynamicResource AccentBrush}'><TextBlock Text='V' Foreground='White' FontSize='21' FontWeight='Bold' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><StackPanel Margin='12,3,0,0'><TextBlock Text='Velvet' Foreground='White' FontSize='16' FontWeight='Bold'/><TextBlock Text='центр приложений' Foreground='#8E91A8' FontSize='11'/></StackPanel></StackPanel>
            <StackPanel Grid.Row='1'>
              <TextBlock Text='ПРИЛОЖЕНИЯ' Foreground='#696C83' FontSize='10' FontWeight='Bold' Margin='2,0,0,9'/>
              <Button x:Name='VaultAppButton' Style='{StaticResource AppNavActive}' Content='◆   Хранилище паролей'/>
              <Button x:Name='MineAppButton' Style='{StaticResource AppNavButton}' Content='⛏   Шахта'/>
              <Button x:Name='CalculatorAppButton' Style='{StaticResource AppNavButton}' Content='⌗   Калькулятор'/>
              <Button x:Name='NotesAppButton' Style='{StaticResource AppNavButton}' Content='▤   Заметки'/>
              <Button x:Name='TasksAppButton' Style='{StaticResource AppNavButton}' Content='✓   Задачи'/>
              <Button x:Name='FinanceAppButton' Style='{StaticResource AppNavButton}' Content='₴   Финансы'/>
              <Button x:Name='FilesAppButton' Style='{StaticResource AppNavButton}' Content='⇄   Конвертер файлов'/>
              <Button x:Name='DocumentsAppButton' Style='{StaticResource AppNavButton}' Content='▧   Документы'/>
            </StackPanel>
            <ScrollViewer Grid.Row='2' VerticalScrollBarVisibility='Auto' Margin='0,16,0,12'><StackPanel>
              <TextBlock Text='ТЕМА' Foreground='#696C83' FontSize='10' FontWeight='Bold' Margin='2,0,0,8'/>
              <ComboBox x:Name='ThemeSelector' Style='{StaticResource ModernComboBox}' SelectedIndex='0'>
                <ComboBoxItem Content='Светлая' Tag='white'/><ComboBoxItem Content='Тёмная' Tag='dark'/><ComboBoxItem Content='Серая' Tag='gray'/><ComboBoxItem Content='Тёмно-зелёная glass' Tag='green'/>
              </ComboBox>
              <Border Background='#24253A' CornerRadius='14' Padding='14' Margin='0,18,0,0'>
                <StackPanel>
                  <Grid><TextBlock Text='Здоровье сейфа' Foreground='#D9DBE7' FontSize='12' FontWeight='SemiBold'/><TextBlock x:Name='HealthScore' Text='100%' Foreground='#78E0BC' FontSize='18' FontWeight='Bold' HorizontalAlignment='Right'/></Grid>
                  <TextBlock Text='СИЛЬНЫЕ' Foreground='#777A91' FontSize='9' Margin='0,13,0,4'/><Grid><Border Background='#34364D' Height='5' CornerRadius='3'/><Border x:Name='StrongBar' Background='#34D399' Height='5' CornerRadius='3' HorizontalAlignment='Left' Width='0'/></Grid><TextBlock x:Name='StrongCount' Text='0' Foreground='#AEB1C1' FontSize='10' HorizontalAlignment='Right' Margin='0,2,0,0'/>
                  <TextBlock Text='СРЕДНИЕ' Foreground='#777A91' FontSize='9' Margin='0,7,0,4'/><Grid><Border Background='#34364D' Height='5' CornerRadius='3'/><Border x:Name='MediumBar' Background='#F59E0B' Height='5' CornerRadius='3' HorizontalAlignment='Left' Width='0'/></Grid><TextBlock x:Name='MediumCount' Text='0' Foreground='#AEB1C1' FontSize='10' HorizontalAlignment='Right' Margin='0,2,0,0'/>
                  <TextBlock Text='СЛАБЫЕ' Foreground='#777A91' FontSize='9' Margin='0,7,0,4'/><Grid><Border Background='#34364D' Height='5' CornerRadius='3'/><Border x:Name='WeakBar' Background='#FB7185' Height='5' CornerRadius='3' HorizontalAlignment='Left' Width='0'/></Grid><TextBlock x:Name='WeakCount' Text='0' Foreground='#AEB1C1' FontSize='10' HorizontalAlignment='Right' Margin='0,2,0,0'/>
                </StackPanel>
              </Border>
            </StackPanel></ScrollViewer>
            <Border Grid.Row='3' Background='#24253A' CornerRadius='14' Padding='14' Margin='0,0,0,8'><StackPanel><TextBlock Text='Автоблокировка' Foreground='#C8CAD8' FontSize='12' FontWeight='SemiBold'/><TextBlock Text='через 10 минут' Foreground='#777A91' FontSize='11' Margin='0,3,0,11'/><Button x:Name='LockButton' Style='{StaticResource GhostButton}' Content='Заблокировать' FontSize='12'/></StackPanel></Border>
          </Grid>
        </Border>
        <Grid x:Name='WorkspacePanel' Grid.Column='1' Margin='30,24,28,25'>
          <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>
          <Grid Grid.Row='0' Margin='0,0,0,22'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><StackPanel><TextBlock Text='Мои пароли' FontSize='28' FontWeight='Bold' Foreground='{DynamicResource Ink}'/><TextBlock Text='Управляйте доступами спокойно и безопасно' Foreground='{DynamicResource Muted}' FontSize='13' Margin='0,5,0,0'/></StackPanel><Button x:Name='AddButton' Grid.Column='1' Style='{StaticResource ActionButton}' Content='+  Новая запись' VerticalAlignment='Center'/></Grid>
          <Grid Grid.Row='1' Margin='0,0,0,18'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='12'/><ColumnDefinition Width='130'/><ColumnDefinition Width='12'/><ColumnDefinition Width='130'/></Grid.ColumnDefinitions>
            <TextBox x:Name='SearchBox' Style='{StaticResource Field}' Height='44' ToolTip='Поиск по названию, логину или сайту'/>
            <Border Grid.Column='2' Background='{DynamicResource SurfaceBrush}' CornerRadius='12' Padding='14,8'><StackPanel><TextBlock Text='ВСЕГО' Foreground='{DynamicResource Muted}' FontSize='9' FontWeight='Bold'/><TextBlock x:Name='TotalCount' Text='0' Foreground='{DynamicResource Ink}' FontSize='18' FontWeight='Bold'/></StackPanel></Border>
            <Border Grid.Column='4' Background='{DynamicResource SurfaceBrush}' CornerRadius='12' Padding='14,8'><StackPanel><TextBlock Text='ИЗБРАННОЕ' Foreground='{DynamicResource Muted}' FontSize='9' FontWeight='Bold'/><TextBlock x:Name='FavoriteCount' Text='0' Foreground='#A567E7' FontSize='18' FontWeight='Bold'/></StackPanel></Border>
          </Grid>
          <Grid Grid.Row='2'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='18'/><ColumnDefinition Width='360'/></Grid.ColumnDefinitions>
            <Border Background='{DynamicResource SurfaceBrush}' CornerRadius='18' Padding='10'>
              <Grid><ListBox x:Name='EntriesList' BorderThickness='0' Background='Transparent' ScrollViewer.HorizontalScrollBarVisibility='Disabled'>
                <ListBox.ItemContainerStyle><Style TargetType='ListBoxItem'><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Padding' Value='0'/><Setter Property='Margin' Value='0,0,0,6'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='Card' Background='{TemplateBinding Background}' CornerRadius='13' Padding='12'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Card' Property='Background' Value='{DynamicResource HoverBrush}'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='Card' Property='Background' Value='{DynamicResource SelectedBrush}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style></ListBox.ItemContainerStyle>
                <ListBox.ItemTemplate><DataTemplate><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='48'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><Border Width='42' Height='42' CornerRadius='13' Background='{DynamicResource SoftAccentBrush}' VerticalAlignment='Center'><TextBlock Text='{Binding Initial}' Foreground='#8B6BE6' FontSize='17' FontWeight='Bold' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><StackPanel Grid.Column='1' Margin='11,1,8,0'><TextBlock Text='{Binding Title}' Foreground='{DynamicResource Ink}' FontSize='14' FontWeight='SemiBold'/><TextBlock Text='{Binding Subtitle}' Foreground='{DynamicResource Muted}' FontSize='12' Margin='0,3,0,0'/></StackPanel><StackPanel Grid.Column='2' HorizontalAlignment='Right'><TextBlock Text='{Binding FavoriteMark}' Foreground='#D7931F' FontSize='15' HorizontalAlignment='Right'/><TextBlock Text='{Binding MaskedPassword}' Foreground='{DynamicResource Muted}' FontSize='10' Margin='0,4,0,0'/></StackPanel></Grid></DataTemplate></ListBox.ItemTemplate>
              </ListBox><TextBlock x:Name='EmptyText' Text='Пока здесь пусто.&#10;Создайте первую запись — это займёт меньше минуты.' Foreground='#A0A2B2' FontSize='14' TextAlignment='Center' VerticalAlignment='Center' HorizontalAlignment='Center' LineHeight='23'/></Grid>
            </Border>
            <Border Grid.Column='2' Background='{DynamicResource SurfaceBrush}' CornerRadius='18' Padding='22'>
              <Grid><Grid x:Name='DetailEmpty'><StackPanel VerticalAlignment='Center'><Border Width='70' Height='70' CornerRadius='24' Background='{DynamicResource SoftAccentBrush}' HorizontalAlignment='Center'><TextBlock Text='◇' Foreground='#8863E9' FontSize='34' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><TextBlock Text='Выберите запись' Foreground='{DynamicResource Ink}' FontSize='17' FontWeight='SemiBold' HorizontalAlignment='Center' Margin='0,17,0,5'/><TextBlock Text='Данные появятся здесь' Foreground='{DynamicResource Muted}' FontSize='12' HorizontalAlignment='Center'/></StackPanel></Grid>
              <ScrollViewer x:Name='DetailEditor' Visibility='Collapsed' VerticalScrollBarVisibility='Auto'><StackPanel>
                <Grid Margin='0,0,0,18'><TextBlock Text='Детали' FontSize='20' FontWeight='Bold' Foreground='{DynamicResource Ink}'/><Button x:Name='DeleteButton' Style='{StaticResource DangerButton}' Content='Удалить' HorizontalAlignment='Right' Padding='12,7' FontSize='11'/></Grid>
                <TextBlock Style='{StaticResource Label}' Text='НАЗВАНИЕ'/><TextBox x:Name='TitleBox' Style='{StaticResource Field}'/>
                <TextBlock Style='{StaticResource Label}' Text='ЛОГИН ИЛИ EMAIL' Margin='0,13,0,6'/><Grid><TextBox x:Name='UsernameBox' Style='{StaticResource Field}' Padding='12,10,70,10'/><Button x:Name='CopyUsernameButton' Style='{StaticResource IconButton}' Content='Копия' FontSize='10' HorizontalAlignment='Right'/></Grid>
                <TextBlock Style='{StaticResource Label}' Text='ПАРОЛЬ' Margin='0,13,0,6'/><Grid><PasswordBox x:Name='PasswordBox' Style='{StaticResource PasswordField}' Padding='12,10,70,10'/><TextBox x:Name='VisiblePasswordBox' Style='{StaticResource Field}' Padding='12,10,70,10' Visibility='Collapsed'/><Button x:Name='CopyPasswordButton' Style='{StaticResource IconButton}' Content='Копия' FontSize='10' HorizontalAlignment='Right'/></Grid>
                <Grid Margin='0,5,0,0'><CheckBox x:Name='ShowPassword' Content='Показать пароль'/><TextBlock x:Name='StrengthText' Text='Надёжность' Foreground='#8B8D9F' FontSize='11' HorizontalAlignment='Right'/></Grid><Border Background='#ECECF3' Height='4' CornerRadius='2' Margin='0,3,0,0'><Border x:Name='StrengthBar' Background='#8B5CF6' CornerRadius='2' HorizontalAlignment='Left' Width='0'/></Border>
                <Border x:Name='HistoryPanel' Visibility='Collapsed' Background='{DynamicResource SoftPanelBrush}' CornerRadius='14' Padding='13' Margin='0,14,0,0'><StackPanel><Grid><TextBlock Text='История паролей' Foreground='{DynamicResource Ink}' FontSize='12' FontWeight='SemiBold'/><TextBlock x:Name='HistoryCount' Text='0 из 10' Foreground='{DynamicResource Muted}' FontSize='10' HorizontalAlignment='Right'/></Grid><ComboBox x:Name='PasswordHistoryBox' Style='{StaticResource ModernComboBox}' Margin='0,9,0,8'><ComboBox.ItemTemplate><DataTemplate><TextBlock Text='{Binding Label}'/></DataTemplate></ComboBox.ItemTemplate></ComboBox><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='8'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Button x:Name='CopyHistoryButton' Style='{StaticResource GhostButton}' Content='Скопировать' FontSize='11' Padding='8,7'/><Button x:Name='RestoreHistoryButton' Grid.Column='2' Style='{StaticResource GhostButton}' Content='Вернуть пароль' FontSize='11' Padding='8,7'/></Grid></StackPanel></Border>
                <Border Background='{DynamicResource SoftPanelBrush}' CornerRadius='14' Padding='14' Margin='0,15,0,0'><StackPanel><Grid><TextBlock Text='Генератор' Foreground='{DynamicResource Ink}' FontWeight='SemiBold'/><TextBlock x:Name='LengthLabel' Text='20 символов' Foreground='{DynamicResource Muted}' FontSize='11' HorizontalAlignment='Right'/></Grid><Slider x:Name='LengthSlider' Minimum='8' Maximum='64' Value='20' TickFrequency='1' IsSnapToTickEnabled='True' Margin='0,8,0,5'/><WrapPanel><CheckBox x:Name='LowerCheck' Content='a-z' IsChecked='True' Margin='0,3,14,3'/><CheckBox x:Name='UpperCheck' Content='A-Z' IsChecked='True' Margin='0,3,14,3'/><CheckBox x:Name='DigitsCheck' Content='0-9' IsChecked='True' Margin='0,3,14,3'/><CheckBox x:Name='SymbolsCheck' Content='!@#' IsChecked='True' Margin='0,3,0,3'/></WrapPanel><Button x:Name='GenerateButton' Style='{StaticResource GhostButton}' Content='Создать сильный пароль' FontSize='12' Margin='0,8,0,0'/></StackPanel></Border>
                <TextBlock Style='{StaticResource Label}' Text='САЙТ' Margin='0,13,0,6'/><TextBox x:Name='WebsiteBox' Style='{StaticResource Field}'/>
                <TextBlock Style='{StaticResource Label}' Text='ЗАМЕТКИ' Margin='0,13,0,6'/><TextBox x:Name='NotesBox' Style='{StaticResource Field}' Height='68' AcceptsReturn='True' TextWrapping='Wrap' VerticalContentAlignment='Top'/>
                <CheckBox x:Name='FavoriteBox' Content='Добавить в избранное' Margin='0,10,0,0'/><TextBlock x:Name='UpdatedLabel' Foreground='#A0A2B0' FontSize='10' Margin='0,5,0,0'/>
                <Button x:Name='SaveButton' Style='{StaticResource ActionButton}' Content='Сохранить изменения' Margin='0,15,0,4'/>
              </StackPanel></ScrollViewer></Grid>
            </Border>
          </Grid>
        </Grid>
        <Grid x:Name='GameView' Grid.Column='1' Visibility='Collapsed' Margin='28,22,28,24'>
          <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>
          <Grid Grid.Row='0' Margin='0,0,0,18'>
            <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
            <StackPanel><TextBlock Text='Шахта' FontSize='28' FontWeight='Bold' Foreground='{DynamicResource Ink}'/><TextBlock Text='Исследуйте шахту клавишами WASD и добывайте ресурсы' Foreground='{DynamicResource Muted}' FontSize='13' Margin='0,5,0,0'/></StackPanel>
            <StackPanel Grid.Column='1' Orientation='Horizontal' VerticalAlignment='Center'>
              <Border Background='{DynamicResource SurfaceBrush}' CornerRadius='12' Padding='16,8' Margin='0,0,10,0'><StackPanel><TextBlock Text='МОНЕТЫ' Foreground='{DynamicResource Muted}' FontSize='9' FontWeight='Bold'/><TextBlock x:Name='GameCoinsText' Text='0' Foreground='#F59E0B' FontSize='18' FontWeight='Bold'/></StackPanel></Border>
              <Border Background='{DynamicResource SurfaceBrush}' CornerRadius='12' Padding='16,8'><StackPanel><TextBlock Text='РУДА' Foreground='{DynamicResource Muted}' FontSize='9' FontWeight='Bold'/><TextBlock x:Name='GameOreText' Text='0 / 20' Foreground='#60A5FA' FontSize='18' FontWeight='Bold'/></StackPanel></Border>
            </StackPanel>
          </Grid>
          <Grid Grid.Row='1'>
            <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='18'/><ColumnDefinition Width='280'/></Grid.ColumnDefinitions>
            <Border Background='#0B1220' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='18' Padding='12'>
              <Viewbox Stretch='Uniform'><Canvas x:Name='GameCanvas' Width='896' Height='512' Background='#111827' ClipToBounds='True' Focusable='True'/></Viewbox>
            </Border>
            <Border Grid.Column='2' Background='{DynamicResource SurfaceBrush}' CornerRadius='18' Padding='20'>
              <Grid><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
                <StackPanel><TextBlock Text='Стив-шахтёр' Foreground='{DynamicResource Ink}' FontSize='20' FontWeight='Bold'/><TextBlock Text='Подойдите к блоку руды — кирка добудет его автоматически.' Foreground='{DynamicResource Muted}' FontSize='12' TextWrapping='Wrap' LineHeight='18' Margin='0,7,0,0'/></StackPanel>
                <Border Grid.Row='1' Background='{DynamicResource SoftPanelBrush}' CornerRadius='13' Padding='13' Margin='0,16,0,14'><StackPanel><Grid><TextBlock Text='Кирка' Foreground='{DynamicResource Muted}'/><TextBlock x:Name='GamePickaxeText' Text='Уровень 1' Foreground='{DynamicResource Ink}' FontWeight='Bold' HorizontalAlignment='Right'/></Grid><Grid Margin='0,8,0,0'><TextBlock Text='Рюкзак' Foreground='{DynamicResource Muted}'/><TextBlock x:Name='GameBackpackText' Text='0 / 20' Foreground='{DynamicResource Ink}' FontWeight='Bold' HorizontalAlignment='Right'/></Grid><TextBlock Text='W A S D — движение' Foreground='#8B8FA4' FontSize='11' Margin='0,10,0,0'/></StackPanel></Border>
                <StackPanel Grid.Row='2'>
                  <Button x:Name='UpgradePickaxeButton' Style='{StaticResource ActionButton}' Content='Улучшение кирки' Margin='0,0,0,8'/>
                  <Button x:Name='BackpackButton' Style='{StaticResource GhostButton}' Content='Рюкзак' Margin='0,0,0,8'/>
                  <Button x:Name='UpgradeBackpackButton' Style='{StaticResource GhostButton}' Content='Улучшить рюкзак' Margin='0,0,0,8'/>
                  <Button x:Name='SellResourcesButton' Style='{StaticResource GhostButton}' Content='Продать ресурсы' Margin='0,0,0,8'/>
                  <Button x:Name='GameStatisticsButton' Style='{StaticResource GhostButton}' Content='Статистика'/>
                </StackPanel>
                <Border Grid.Row='3' Background='#182234' CornerRadius='12' Padding='12' Margin='0,15,0,0'><TextBlock x:Name='GameStatusText' Text='Найдите первый блок руды.' Foreground='#C7D2FE' FontSize='11' TextWrapping='Wrap' TextAlignment='Center'/></Border>
              </Grid>
            </Border>
          </Grid>
        </Grid>

        <Grid x:Name='CalculatorView' Grid.Column='1' Visibility='Collapsed' Margin='30,24,30,28'>
          <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>
          <StackPanel Margin='0,0,0,20'><TextBlock Text='Калькулятор' FontSize='28' FontWeight='Bold' Foreground='{DynamicResource Ink}'/><TextBlock Text='Быстрые вычисления прямо внутри Velvet' Foreground='{DynamicResource Muted}' FontSize='13' Margin='0,5,0,0'/></StackPanel>
          <Border Grid.Row='1' Width='460' Background='{DynamicResource SurfaceBrush}' CornerRadius='22' Padding='24' HorizontalAlignment='Center' VerticalAlignment='Center'>
            <Grid><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='18'/><RowDefinition Height='*'/></Grid.RowDefinitions>
              <TextBox x:Name='CalculatorDisplay' Text='0' Style='{StaticResource Field}' FontSize='30' FontWeight='SemiBold' Height='72' IsReadOnly='True' TextAlignment='Right' Padding='16,10'/>
              <UniformGrid Grid.Row='2' Columns='4' Rows='5'>
                <Button x:Name='CalcClear' Style='{StaticResource DangerButton}' Content='C' Margin='4'/><Button x:Name='CalcBack' Style='{StaticResource GhostButton}' Content='⌫' Margin='4'/><Button x:Name='CalcDivide' Style='{StaticResource GhostButton}' Content='÷' Margin='4'/><Button x:Name='CalcMultiply' Style='{StaticResource GhostButton}' Content='×' Margin='4'/>
                <Button x:Name='Calc7' Style='{StaticResource GhostButton}' Content='7' Margin='4'/><Button x:Name='Calc8' Style='{StaticResource GhostButton}' Content='8' Margin='4'/><Button x:Name='Calc9' Style='{StaticResource GhostButton}' Content='9' Margin='4'/><Button x:Name='CalcMinus' Style='{StaticResource GhostButton}' Content='−' Margin='4'/>
                <Button x:Name='Calc4' Style='{StaticResource GhostButton}' Content='4' Margin='4'/><Button x:Name='Calc5' Style='{StaticResource GhostButton}' Content='5' Margin='4'/><Button x:Name='Calc6' Style='{StaticResource GhostButton}' Content='6' Margin='4'/><Button x:Name='CalcPlus' Style='{StaticResource GhostButton}' Content='+' Margin='4'/>
                <Button x:Name='Calc1' Style='{StaticResource GhostButton}' Content='1' Margin='4'/><Button x:Name='Calc2' Style='{StaticResource GhostButton}' Content='2' Margin='4'/><Button x:Name='Calc3' Style='{StaticResource GhostButton}' Content='3' Margin='4'/><Button x:Name='CalcEquals' Style='{StaticResource ActionButton}' Content='=' Margin='4'/>
                <Button x:Name='CalcSign' Style='{StaticResource GhostButton}' Content='±' Margin='4'/><Button x:Name='Calc0' Style='{StaticResource GhostButton}' Content='0' Margin='4'/><Button x:Name='Calc00' Style='{StaticResource GhostButton}' Content='00' Margin='4'/><Button x:Name='CalcDot' Style='{StaticResource GhostButton}' Content='.' Margin='4'/>
              </UniformGrid>
            </Grid>
          </Border>
        </Grid>

        <Grid x:Name='NotesView' Grid.Column='1' Visibility='Collapsed' Margin='30,24,28,25'>
          <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>
          <Grid Margin='0,0,0,20'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><StackPanel><TextBlock Text='Заметки' FontSize='28' FontWeight='Bold' Foreground='{DynamicResource Ink}'/><TextBlock Text='Идеи и записи сохраняются автоматически на этом компьютере' Foreground='{DynamicResource Muted}' FontSize='13' Margin='0,5,0,0'/></StackPanel><StackPanel Grid.Column='1' Orientation='Horizontal'><Button x:Name='NewNoteButton' Style='{StaticResource ActionButton}' Content='+ Новая заметка' Margin='0,0,10,0'/><Button x:Name='DeleteNoteButton' Style='{StaticResource DangerButton}' Content='Удалить'/></StackPanel></Grid>
          <Grid Grid.Row='1'><Grid.ColumnDefinitions><ColumnDefinition Width='320'/><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
            <Border Background='{DynamicResource SurfaceBrush}' CornerRadius='18' Padding='10'><ListBox x:Name='NotesList' DisplayMemberPath='Title' Background='Transparent' BorderThickness='0'/></Border>
            <Border Grid.Column='2' Background='{DynamicResource SurfaceBrush}' CornerRadius='18' Padding='22'><Grid><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='14'/><RowDefinition Height='*'/><RowDefinition Height='14'/><RowDefinition Height='Auto'/></Grid.RowDefinitions><TextBox x:Name='NoteTitleBox' Style='{StaticResource Field}' FontSize='18' FontWeight='SemiBold' ToolTip='Название заметки'/><TextBox x:Name='NoteTextBox' Grid.Row='2' Style='{StaticResource Field}' AcceptsReturn='True' TextWrapping='Wrap' VerticalContentAlignment='Top' VerticalScrollBarVisibility='Auto' ToolTip='Текст заметки'/><Button x:Name='SaveNoteButton' Grid.Row='4' Style='{StaticResource ActionButton}' Content='Сохранить заметку' HorizontalAlignment='Right'/></Grid></Border>
          </Grid>
        </Grid>

        <Grid x:Name='TasksView' Grid.Column='1' Visibility='Collapsed' Margin='30,24,28,25'>
          <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
          <StackPanel Margin='0,0,0,20'><TextBlock Text='Задачи' FontSize='28' FontWeight='Bold' Foreground='{DynamicResource Ink}'/><TextBlock Text='Простой список дел с сохранением прогресса' Foreground='{DynamicResource Muted}' FontSize='13' Margin='0,5,0,0'/></StackPanel>
          <Grid Grid.Row='1' Margin='0,0,0,16'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='12'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><TextBox x:Name='TaskInput' Style='{StaticResource Field}' Height='46' ToolTip='Введите новую задачу'/><Button x:Name='AddTaskButton' Grid.Column='2' Style='{StaticResource ActionButton}' Content='+ Добавить задачу'/></Grid>
          <Border Grid.Row='2' Background='{DynamicResource SurfaceBrush}' CornerRadius='18' Padding='12'><ListBox x:Name='TasksList' DisplayMemberPath='DisplayTitle' Background='Transparent' BorderThickness='0' FontSize='15'/></Border>
          <StackPanel Grid.Row='3' Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,16,0,0'><Button x:Name='ToggleTaskButton' Style='{StaticResource GhostButton}' Content='Изменить статус' Margin='0,0,10,0'/><Button x:Name='DeleteTaskButton' Style='{StaticResource DangerButton}' Content='Удалить'/></StackPanel>
        </Grid>

        <Grid x:Name='FinanceView' Grid.Column='1' Visibility='Collapsed' Margin='30,24,28,25'>
          <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
          <Grid Margin='0,0,0,20'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><StackPanel><TextBlock Text='Финансы' FontSize='28' FontWeight='Bold' Foreground='{DynamicResource Ink}'/><TextBlock Text='Локальный учёт доходов и расходов' Foreground='{DynamicResource Muted}' FontSize='13' Margin='0,5,0,0'/></StackPanel><Border Grid.Column='1' Background='{DynamicResource SurfaceBrush}' CornerRadius='14' Padding='18,10'><StackPanel><TextBlock Text='БАЛАНС' Foreground='{DynamicResource Muted}' FontSize='9' FontWeight='Bold'/><TextBlock x:Name='FinanceBalanceText' Text='0.00 ₴' Foreground='#34D399' FontSize='22' FontWeight='Bold'/></StackPanel></Border></Grid>
          <Grid Grid.Row='1' Margin='0,0,0,16'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='12'/><ColumnDefinition Width='180'/><ColumnDefinition Width='12'/><ColumnDefinition Width='170'/><ColumnDefinition Width='12'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><TextBox x:Name='FinanceDescriptionBox' Style='{StaticResource Field}' Height='46' ToolTip='Описание операции'/><TextBox x:Name='FinanceAmountBox' Grid.Column='2' Style='{StaticResource Field}' Height='46' ToolTip='Сумма'/><ComboBox x:Name='FinanceTypeBox' Grid.Column='4' Style='{StaticResource ModernComboBox}' SelectedIndex='0'><ComboBoxItem Content='Доход'/><ComboBoxItem Content='Расход'/></ComboBox><Button x:Name='AddFinanceButton' Grid.Column='6' Style='{StaticResource ActionButton}' Content='Добавить'/></Grid>
          <Border Grid.Row='2' Background='{DynamicResource SurfaceBrush}' CornerRadius='18' Padding='12'><ListBox x:Name='FinanceList' DisplayMemberPath='DisplayText' Background='Transparent' BorderThickness='0' FontSize='14'/></Border>
          <Button x:Name='DeleteFinanceButton' Grid.Row='3' Style='{StaticResource DangerButton}' Content='Удалить выбранную операцию' HorizontalAlignment='Right' Margin='0,16,0,0'/>
        </Grid>

        <Grid x:Name='ConverterView' Grid.Column='1' Visibility='Collapsed' Margin='30,24,28,25'>
          <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>
          <StackPanel Margin='0,0,0,20'><TextBlock Text='Конвертер изображений' FontSize='28' FontWeight='Bold' Foreground='{DynamicResource Ink}'/><TextBlock Text='Преобразование PNG, JPG, BMP, GIF и TIFF без отправки файлов в интернет' Foreground='{DynamicResource Muted}' FontSize='13' Margin='0,5,0,0'/></StackPanel>
          <Border Grid.Row='1' Width='650' Background='{DynamicResource SurfaceBrush}' CornerRadius='22' Padding='30' HorizontalAlignment='Center' VerticalAlignment='Center'><StackPanel><Border Background='{DynamicResource SoftPanelBrush}' CornerRadius='15' Padding='18'><TextBlock x:Name='ConverterSourceText' Text='Файл пока не выбран' Foreground='{DynamicResource Muted}' TextWrapping='Wrap' TextAlignment='Center'/></Border><Button x:Name='ChooseConverterFileButton' Style='{StaticResource GhostButton}' Content='Выбрать изображение' Margin='0,18,0,12'/><TextBlock Style='{StaticResource Label}' Text='ФОРМАТ РЕЗУЛЬТАТА'/><ComboBox x:Name='ConverterFormatBox' Style='{StaticResource ModernComboBox}' SelectedIndex='0'><ComboBoxItem Content='PNG' Tag='png'/><ComboBoxItem Content='JPEG' Tag='jpg'/><ComboBoxItem Content='BMP' Tag='bmp'/><ComboBoxItem Content='GIF' Tag='gif'/><ComboBoxItem Content='TIFF' Tag='tiff'/></ComboBox><Button x:Name='ConvertFileButton' Style='{StaticResource ActionButton}' Content='Конвертировать и сохранить' Margin='0,18,0,0'/></StackPanel></Border>
        </Grid>

        <Grid x:Name='DocumentsView' Grid.Column='1' Visibility='Collapsed' Margin='30,24,28,25'>
          <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>
          <Grid Margin='0,0,0,20'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><StackPanel><TextBlock Text='Документы' FontSize='28' FontWeight='Bold' Foreground='{DynamicResource Ink}'/><TextBlock Text='Создавайте документы и экспортируйте их в TXT' Foreground='{DynamicResource Muted}' FontSize='13' Margin='0,5,0,0'/></StackPanel><StackPanel Grid.Column='1' Orientation='Horizontal'><Button x:Name='NewDocumentButton' Style='{StaticResource ActionButton}' Content='+ Новый документ' Margin='0,0,10,0'/><Button x:Name='DeleteDocumentButton' Style='{StaticResource DangerButton}' Content='Удалить'/></StackPanel></Grid>
          <Grid Grid.Row='1'><Grid.ColumnDefinitions><ColumnDefinition Width='320'/><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Border Background='{DynamicResource SurfaceBrush}' CornerRadius='18' Padding='10'><ListBox x:Name='DocumentsList' DisplayMemberPath='Title' Background='Transparent' BorderThickness='0'/></Border><Border Grid.Column='2' Background='{DynamicResource SurfaceBrush}' CornerRadius='18' Padding='22'><Grid><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='14'/><RowDefinition Height='*'/><RowDefinition Height='14'/><RowDefinition Height='Auto'/></Grid.RowDefinitions><TextBox x:Name='DocumentTitleBox' Style='{StaticResource Field}' FontSize='18' FontWeight='SemiBold' ToolTip='Название документа'/><TextBox x:Name='DocumentContentBox' Grid.Row='2' Style='{StaticResource Field}' AcceptsReturn='True' TextWrapping='Wrap' VerticalContentAlignment='Top' VerticalScrollBarVisibility='Auto' ToolTip='Текст документа'/><StackPanel Grid.Row='4' Orientation='Horizontal' HorizontalAlignment='Right'><Button x:Name='ExportDocumentButton' Style='{StaticResource GhostButton}' Content='Экспорт TXT' Margin='0,0,10,0'/><Button x:Name='SaveDocumentButton' Style='{StaticResource ActionButton}' Content='Сохранить документ'/></StackPanel></Grid></Border></Grid>
        </Grid>
      </Grid>
      <Grid x:Name='DialogOverlay' Grid.RowSpan='2' Panel.ZIndex='80' Visibility='Collapsed' Opacity='0' Background='#990B0C14'>
        <Border x:Name='DialogCard' Width='430' Background='{DynamicResource SurfaceBrush}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='24' Padding='28' HorizontalAlignment='Center' VerticalAlignment='Center' RenderTransformOrigin='.5,.5'>
          <Border.RenderTransform><ScaleTransform ScaleX='.9' ScaleY='.9'/></Border.RenderTransform>
          <StackPanel>
            <Border Width='54' Height='54' Background='{DynamicResource DangerBrush}' CornerRadius='18' HorizontalAlignment='Left'><TextBlock Text='!' Foreground='{DynamicResource DangerTextBrush}' FontSize='25' FontWeight='Bold' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
            <TextBlock x:Name='DialogTitle' Text='Подтверждение' Foreground='{DynamicResource Ink}' FontSize='22' FontWeight='Bold' Margin='0,20,0,8'/>
            <TextBlock x:Name='DialogMessage' Text='Продолжить действие?' Foreground='{DynamicResource Muted}' FontSize='13' TextWrapping='Wrap' LineHeight='20'/>
            <Grid Margin='0,25,0,0'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='12'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Button x:Name='DialogCancelButton' Style='{StaticResource GhostButton}' Content='Отмена'/><Button x:Name='DialogConfirmButton' Grid.Column='2' Style='{StaticResource DangerButton}' Content='Удалить'/></Grid>
          </StackPanel>
        </Border>
      </Grid>
      <Border x:Name='Toast' Grid.Row='1' Background='#232438' CornerRadius='12' Padding='18,11' HorizontalAlignment='Center' VerticalAlignment='Bottom' Margin='0,0,0,24' Opacity='0' IsHitTestVisible='False'><TextBlock x:Name='ToastText' Foreground='White' FontSize='13' FontWeight='SemiBold'/></Border>
    </Grid>
  </Border>
</Window>";
            Window parsed = (Window)XamlReader.Parse(xaml);
            parsedWindow = parsed;
            this.Title = parsed.Title; this.Width = parsed.Width; this.Height = parsed.Height; this.MinWidth = parsed.MinWidth; this.MinHeight = parsed.MinHeight;
            this.WindowStyle = parsed.WindowStyle; this.AllowsTransparency = parsed.AllowsTransparency; this.Background = parsed.Background; this.ResizeMode = parsed.ResizeMode; this.FontFamily = parsed.FontFamily;
            this.Content = parsed.Content;
            this.Resources = parsed.Resources;
            this.WindowState = WindowState.Maximized;

            loginView = Find<Grid>("LoginView"); mainView = Find<Grid>("MainView"); loginTitle = Find<TextBlock>("LoginTitle"); loginSubtitle = Find<TextBlock>("LoginSubtitle"); loginPassword = Find<PasswordBox>("LoginPassword"); confirmPassword = Find<PasswordBox>("ConfirmPassword"); confirmPanel = Find<FrameworkElement>("ConfirmPanel"); loginButton = Find<Button>("LoginButton"); loginError = Find<TextBlock>("LoginError"); searchBox = Find<TextBox>("SearchBox"); entriesList = Find<ListBox>("EntriesList"); totalCount = Find<TextBlock>("TotalCount"); favoriteCount = Find<TextBlock>("FavoriteCount"); emptyText = Find<TextBlock>("EmptyText"); detailEmpty = Find<Grid>("DetailEmpty"); detailEditor = Find<ScrollViewer>("DetailEditor"); titleBox = Find<TextBox>("TitleBox"); usernameBox = Find<TextBox>("UsernameBox"); passwordBox = Find<PasswordBox>("PasswordBox"); visiblePasswordBox = Find<TextBox>("VisiblePasswordBox"); showPassword = Find<CheckBox>("ShowPassword"); websiteBox = Find<TextBox>("WebsiteBox"); notesBox = Find<TextBox>("NotesBox"); favoriteBox = Find<CheckBox>("FavoriteBox"); lengthSlider = Find<Slider>("LengthSlider"); lengthLabel = Find<TextBlock>("LengthLabel"); lowerCheck = Find<CheckBox>("LowerCheck"); upperCheck = Find<CheckBox>("UpperCheck"); digitsCheck = Find<CheckBox>("DigitsCheck"); symbolsCheck = Find<CheckBox>("SymbolsCheck"); strengthText = Find<TextBlock>("StrengthText"); strengthBar = Find<Border>("StrengthBar"); updatedLabel = Find<TextBlock>("UpdatedLabel"); toast = Find<Border>("Toast"); toastText = Find<TextBlock>("ToastText");
            historyPanel = Find<Border>("HistoryPanel"); passwordHistoryBox = Find<ComboBox>("PasswordHistoryBox"); historyCount = Find<TextBlock>("HistoryCount");
            dialogOverlay = Find<Grid>("DialogOverlay"); dialogCard = Find<Border>("DialogCard"); dialogTitle = Find<TextBlock>("DialogTitle"); dialogMessage = Find<TextBlock>("DialogMessage"); dialogCancelButton = Find<Button>("DialogCancelButton"); dialogConfirmButton = Find<Button>("DialogConfirmButton");
            loginProgress = Find<Grid>("LoginProgress"); loginProgressGlow = Find<Border>("LoginProgressGlow");
            themeSelector = Find<ComboBox>("ThemeSelector"); healthScore = Find<TextBlock>("HealthScore"); strongCount = Find<TextBlock>("StrongCount"); mediumCount = Find<TextBlock>("MediumCount"); weakCount = Find<TextBlock>("WeakCount"); strongBar = Find<Border>("StrongBar"); mediumBar = Find<Border>("MediumBar"); weakBar = Find<Border>("WeakBar"); ambientCanvas = Find<Canvas>("AmbientCanvas"); sidebarPanel = Find<FrameworkElement>("SidebarPanel"); workspacePanel = Find<FrameworkElement>("WorkspacePanel");
            gameView = Find<Grid>("GameView"); gameCanvas = Find<Canvas>("GameCanvas"); gameCoinsText = Find<TextBlock>("GameCoinsText"); gameOreText = Find<TextBlock>("GameOreText"); gamePickaxeText = Find<TextBlock>("GamePickaxeText"); gameBackpackText = Find<TextBlock>("GameBackpackText"); gameStatusText = Find<TextBlock>("GameStatusText");
            calculatorView = Find<Grid>("CalculatorView"); calculatorDisplay = Find<TextBox>("CalculatorDisplay");
            notesView = Find<Grid>("NotesView"); notesList = Find<ListBox>("NotesList"); noteTitleBox = Find<TextBox>("NoteTitleBox"); noteTextBox = Find<TextBox>("NoteTextBox");
            tasksView = Find<Grid>("TasksView"); tasksList = Find<ListBox>("TasksList"); taskInput = Find<TextBox>("TaskInput");
            financeView = Find<Grid>("FinanceView"); financeList = Find<ListBox>("FinanceList"); financeDescriptionBox = Find<TextBox>("FinanceDescriptionBox"); financeAmountBox = Find<TextBox>("FinanceAmountBox"); financeTypeBox = Find<ComboBox>("FinanceTypeBox"); financeBalanceText = Find<TextBlock>("FinanceBalanceText");
            converterView = Find<Grid>("ConverterView"); converterSourceText = Find<TextBlock>("ConverterSourceText"); converterFormatBox = Find<ComboBox>("ConverterFormatBox");
            documentsView = Find<Grid>("DocumentsView"); documentsList = Find<ListBox>("DocumentsList"); documentTitleBox = Find<TextBox>("DocumentTitleBox"); documentContentBox = Find<TextBox>("DocumentContentBox");
        }

        private T Find<T>(string name) where T : FrameworkElement { return (T)parsedWindow.FindName(name); }

        private void WireEvents()
        {
            Find<Border>("TitleBar").MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else if (WindowState == WindowState.Normal) DragMove(); };
            Find<Button>("MinButton").Click += delegate { WindowState = WindowState.Minimized; };
            Find<Button>("MaxButton").Click += delegate { ToggleMaximize(); };
            Find<Button>("CloseButton").Click += delegate { Close(); };
            Find<Button>("VaultAppButton").Click += delegate { ShowVaultWorkspace(); };
            Find<Button>("MineAppButton").Click += delegate { ShowMineGame(); };
            Find<Button>("CalculatorAppButton").Click += delegate { ShowModule(calculatorView, "CalculatorAppButton"); };
            Find<Button>("NotesAppButton").Click += delegate { ShowModule(notesView, "NotesAppButton"); };
            Find<Button>("TasksAppButton").Click += delegate { ShowModule(tasksView, "TasksAppButton"); taskInput.Focus(); };
            Find<Button>("FinanceAppButton").Click += delegate { ShowModule(financeView, "FinanceAppButton"); };
            Find<Button>("FilesAppButton").Click += delegate { ShowModule(converterView, "FilesAppButton"); };
            Find<Button>("DocumentsAppButton").Click += delegate { ShowModule(documentsView, "DocumentsAppButton"); };
            Find<Button>("UpgradePickaxeButton").Click += delegate { UpgradePickaxe(); };
            Find<Button>("BackpackButton").Click += delegate { ShowBackpack(); };
            Find<Button>("UpgradeBackpackButton").Click += delegate { UpgradeBackpack(); };
            Find<Button>("SellResourcesButton").Click += delegate { SellResources(); };
            Find<Button>("GameStatisticsButton").Click += delegate { ShowGameStatistics(); };
            Find<Button>("NewNoteButton").Click += delegate { AddNote(); };
            Find<Button>("SaveNoteButton").Click += delegate { SaveSelectedNote(); };
            Find<Button>("DeleteNoteButton").Click += delegate { DeleteSelectedNote(); };
            notesList.SelectionChanged += delegate { SelectNote(notesList.SelectedItem as NoteItem); };
            noteTitleBox.TextChanged += delegate { AutoSaveSelectedNote(); };
            noteTextBox.TextChanged += delegate { AutoSaveSelectedNote(); };
            Find<Button>("AddTaskButton").Click += delegate { AddTask(); };
            Find<Button>("ToggleTaskButton").Click += delegate { ToggleSelectedTask(); };
            Find<Button>("DeleteTaskButton").Click += delegate { DeleteSelectedTask(); };
            taskInput.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { AddTask(); e.Handled = true; } };
            Find<Button>("AddFinanceButton").Click += delegate { AddFinanceOperation(); };
            Find<Button>("DeleteFinanceButton").Click += delegate { DeleteFinanceOperation(); };
            financeAmountBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { AddFinanceOperation(); e.Handled = true; } };
            Find<Button>("ChooseConverterFileButton").Click += delegate { ChooseConverterFile(); };
            Find<Button>("ConvertFileButton").Click += delegate { ConvertSelectedFile(); };
            Find<Button>("NewDocumentButton").Click += delegate { AddDocument(); };
            Find<Button>("SaveDocumentButton").Click += delegate { SaveSelectedDocument(); };
            Find<Button>("DeleteDocumentButton").Click += delegate { DeleteSelectedDocument(); };
            Find<Button>("ExportDocumentButton").Click += delegate { ExportSelectedDocument(); };
            documentsList.SelectionChanged += delegate { SelectDocument(documentsList.SelectedItem as DocumentItem); };
            WireCalculatorButtons();
            loginButton.Click += LoginButton_Click;
            loginPassword.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) LoginButton_Click(s, e); };
            confirmPassword.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) LoginButton_Click(s, e); };
            Find<Button>("LockButton").Click += delegate { LockVault("Хранилище заблокировано"); };
            Find<Button>("AddButton").Click += delegate { AddEntry(); };
            Find<Button>("SaveButton").Click += delegate { SaveSelected(); };
            Find<Button>("DeleteButton").Click += delegate { DeleteSelected(); };
            Find<Button>("GenerateButton").Click += delegate { SetPassword(GeneratePassword()); ShowToast("Новый пароль создан"); };
            Find<Button>("CopyUsernameButton").Click += delegate { CopySecure(usernameBox.Text, "Логин скопирован"); };
            Find<Button>("CopyPasswordButton").Click += delegate { CopySecure(GetPassword(), "Пароль скопирован на 30 секунд"); };
            Find<Button>("CopyHistoryButton").Click += delegate { PasswordHistoryItem item = passwordHistoryBox.SelectedItem as PasswordHistoryItem; if (item != null) CopySecure(item.Password, "Старый пароль скопирован на 30 секунд"); };
            Find<Button>("RestoreHistoryButton").Click += delegate { PasswordHistoryItem item = passwordHistoryBox.SelectedItem as PasswordHistoryItem; if (item != null) { SetPassword(item.Password); ShowToast("Старый пароль восстановлен — нажмите «Сохранить»"); } };
            dialogCancelButton.Click += delegate { HideDialog(); };
            dialogConfirmButton.Click += delegate { Action action = dialogAction; CloseDialogImmediately(); if (action != null) action(); };
            entriesList.SelectionChanged += delegate { SelectEntry(entriesList.SelectedItem as VaultEntry); };
            searchBox.TextChanged += delegate { if (entriesView != null) entriesView.Refresh(); };
            lengthSlider.ValueChanged += delegate { if (lengthLabel != null) lengthLabel.Text = ((int)lengthSlider.Value).ToString() + " символов"; };
            showPassword.Checked += delegate { visiblePasswordBox.Text = passwordBox.Password; passwordBox.Visibility = Visibility.Collapsed; visiblePasswordBox.Visibility = Visibility.Visible; };
            showPassword.Unchecked += delegate { passwordBox.Password = visiblePasswordBox.Text; visiblePasswordBox.Visibility = Visibility.Collapsed; passwordBox.Visibility = Visibility.Visible; };
            passwordBox.PasswordChanged += delegate { if (showPassword.IsChecked != true) UpdateStrength(passwordBox.Password); };
            visiblePasswordBox.TextChanged += delegate { if (showPassword.IsChecked == true) UpdateStrength(visiblePasswordBox.Text); };
            themeSelector.SelectionChanged += delegate { ApplySelectedTheme(true); };
            PreviewMouseDown += delegate { lastActivity = DateTime.Now; };
            PreviewKeyDown += delegate(object s, KeyEventArgs e) { lastActivity = DateTime.Now; if (e.Key == Key.Escape && dialogOverlay.Visibility == Visibility.Visible) { HideDialog(); e.Handled = true; return; } if (calculatorView.Visibility == Visibility.Visible && HandleCalculatorKey(e)) { e.Handled = true; return; } if (gameView.Visibility == Visibility.Visible && HandleGameKey(e.Key)) e.Handled = true; };
            StateChanged += delegate { UpdateWindowChrome(); };
            Loaded += delegate { UpdateWindowChrome(); BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))); loginPassword.Focus(); };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.F11) ToggleMaximize(); };
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            Dispatcher.BeginInvoke(new Action(UpdateWindowChrome), DispatcherPriority.Loaded);
        }

        private void UpdateWindowChrome()
        {
            bool maximized = WindowState == WindowState.Maximized;
            Find<Border>("ShellBorder").CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(22);
            Find<Border>("TitleBar").CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(22, 22, 0, 0);
            Find<Border>("SidebarPanel").CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(0, 0, 0, 22);
            Find<Button>("MaxButton").Content = maximized ? "❐" : "□";
            Find<Button>("MaxButton").ToolTip = maximized ? "Восстановить окно" : "Развернуть на весь экран";
        }

        private void ShowFutureApp(string appName)
        {
            ShowToast("Модуль «" + appName + "» будет добавлен следующим");
        }

        private void ShowVaultWorkspace()
        {
            ShowModule(workspacePanel, "VaultAppButton");
        }

        private void ShowMineGame()
        {
            ShowModule(gameView, "MineAppButton");
            UpdateGameUi();
            RenderMineGame();
            gameStatusText.Text = "Двигайтесь клавишами WASD. Руда добывается при входе в блок.";
            Dispatcher.BeginInvoke(new Action(delegate { gameCanvas.Focus(); }), DispatcherPriority.Input);
        }

        private void ShowModule(FrameworkElement module, string activeButtonName)
        {
            FrameworkElement[] modules = new FrameworkElement[] { workspacePanel, gameView, calculatorView, notesView, tasksView, financeView, converterView, documentsView };
            foreach (FrameworkElement item in modules) if (item != null) item.Visibility = item == module ? Visibility.Visible : Visibility.Collapsed;
            string[] buttons = new string[] { "VaultAppButton", "MineAppButton", "CalculatorAppButton", "NotesAppButton", "TasksAppButton", "FinanceAppButton", "FilesAppButton", "DocumentsAppButton" };
            foreach (string name in buttons) Find<Button>(name).Style = (Style)Resources[name == activeButtonName ? "AppNavActive" : "AppNavButton"];
            if (module != null) Fade(module);
            lastActivity = DateTime.Now;
        }

        private void InitializeMineGame()
        {
            LoadMineGame();
            int[,] starterOre = new int[,] { { 2, 1 }, { 4, 1 }, { 6, 2 }, { 9, 1 }, { 12, 2 }, { 3, 4 }, { 7, 4 }, { 10, 5 }, { 1, 6 }, { 5, 6 }, { 9, 7 }, { 12, 6 } };
            for (int i = 0; i < starterOre.GetLength(0); i++) gameOreCells.Add(GameCell(starterOre[i, 0], starterOre[i, 1]));
            EnsureGameOre();
            UpdateGameUi();
            RenderMineGame();
        }

        private string GameCell(int x, int y) { return x.ToString(CultureInfo.InvariantCulture) + ":" + y.ToString(CultureInfo.InvariantCulture); }

        private void EnsureGameOre()
        {
            int attempts = 0;
            while (gameOreCells.Count < 18 && attempts++ < 200)
            {
                int x = gameRandom.Next(0, GameColumns);
                int y = gameRandom.Next(0, GameRows);
                if (x == gamePlayerX && y == gamePlayerY) continue;
                gameOreCells.Add(GameCell(x, y));
            }
        }

        private bool HandleGameKey(Key key)
        {
            int dx = 0, dy = 0;
            if (key == Key.W || key == Key.Up) dy = -1;
            else if (key == Key.S || key == Key.Down) dy = 1;
            else if (key == Key.A || key == Key.Left) dx = -1;
            else if (key == Key.D || key == Key.Right) dx = 1;
            else return false;
            MoveMiner(dx, dy);
            return true;
        }

        private void MoveMiner(int dx, int dy)
        {
            int nextX = Math.Max(0, Math.Min(GameColumns - 1, gamePlayerX + dx));
            int nextY = Math.Max(0, Math.Min(GameRows - 1, gamePlayerY + dy));
            if (nextX == gamePlayerX && nextY == gamePlayerY) { gameStatusText.Text = "Здесь край шахты."; return; }

            string cell = GameCell(nextX, nextY);
            if (gameOreCells.Contains(cell))
            {
                int free = gameBackpackCapacity - gameOre;
                if (free <= 0) { gameStatusText.Text = "Рюкзак заполнен. Продайте ресурсы или улучшите его."; return; }
                int mined = Math.Min(free, gamePickaxeLevel);
                gameOre += mined;
                gameTotalMined += mined;
                gameOreCells.Remove(cell);
                gameStatusText.Text = "Добыто руды: " + mined.ToString(CultureInfo.InvariantCulture) + ".";
            }
            else gameStatusText.Text = "Вы исследуете шахту...";

            gamePlayerX = nextX;
            gamePlayerY = nextY;
            gameSteps++;
            EnsureGameOre();
            UpdateGameUi();
            RenderMineGame();
            SaveMineGame();
        }

        private void UpgradePickaxe()
        {
            int cost = 20 * gamePickaxeLevel * gamePickaxeLevel;
            if (gameCoins < cost) { gameStatusText.Text = "Для улучшения кирки нужно " + cost.ToString(CultureInfo.InvariantCulture) + " монет."; return; }
            gameCoins -= cost;
            gamePickaxeLevel++;
            gameStatusText.Text = "Кирка улучшена до уровня " + gamePickaxeLevel.ToString(CultureInfo.InvariantCulture) + ".";
            UpdateGameUi(); SaveMineGame();
        }

        private void ShowBackpack()
        {
            gameStatusText.Text = "В рюкзаке " + gameOre.ToString(CultureInfo.InvariantCulture) + " руды из " + gameBackpackCapacity.ToString(CultureInfo.InvariantCulture) + ".";
        }

        private void UpgradeBackpack()
        {
            int cost = gameBackpackCapacity * 2;
            if (gameCoins < cost) { gameStatusText.Text = "Для улучшения рюкзака нужно " + cost.ToString(CultureInfo.InvariantCulture) + " монет."; return; }
            gameCoins -= cost;
            gameBackpackCapacity += 10;
            gameStatusText.Text = "Рюкзак расширен до " + gameBackpackCapacity.ToString(CultureInfo.InvariantCulture) + " единиц.";
            UpdateGameUi(); SaveMineGame();
        }

        private void SellResources()
        {
            if (gameOre <= 0) { gameStatusText.Text = "В рюкзаке пока нет ресурсов для продажи."; return; }
            int earned = gameOre * (2 + gamePickaxeLevel / 2);
            gameTotalSold += gameOre;
            gameCoins += earned;
            gameOre = 0;
            gameStatusText.Text = "Ресурсы проданы за " + earned.ToString(CultureInfo.InvariantCulture) + " монет.";
            UpdateGameUi(); SaveMineGame();
        }

        private void ShowGameStatistics()
        {
            gameStatusText.Text = "Добыто: " + gameTotalMined.ToString(CultureInfo.InvariantCulture) + " · Продано: " + gameTotalSold.ToString(CultureInfo.InvariantCulture) + " · Шагов: " + gameSteps.ToString(CultureInfo.InvariantCulture);
        }

        private void UpdateGameUi()
        {
            if (gameCoinsText == null) return;
            gameCoinsText.Text = gameCoins.ToString(CultureInfo.InvariantCulture);
            gameOreText.Text = gameOre.ToString(CultureInfo.InvariantCulture) + " / " + gameBackpackCapacity.ToString(CultureInfo.InvariantCulture);
            gamePickaxeText.Text = "Уровень " + gamePickaxeLevel.ToString(CultureInfo.InvariantCulture);
            gameBackpackText.Text = gameOre.ToString(CultureInfo.InvariantCulture) + " / " + gameBackpackCapacity.ToString(CultureInfo.InvariantCulture);
            Find<Button>("UpgradePickaxeButton").Content = "Улучшение кирки · " + (20 * gamePickaxeLevel * gamePickaxeLevel).ToString(CultureInfo.InvariantCulture) + " монет";
            Find<Button>("UpgradeBackpackButton").Content = "Улучшить рюкзак · " + (gameBackpackCapacity * 2).ToString(CultureInfo.InvariantCulture) + " монет";
        }

        private Brush GameBrush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }

        private void RenderMineGame()
        {
            if (gameCanvas == null) return;
            gameCanvas.Children.Clear();
            for (int y = 0; y < GameRows; y++)
            {
                for (int x = 0; x < GameColumns; x++)
                {
                    Border tile = new Border { Width = GameTileSize, Height = GameTileSize, Background = GameBrush((x + y) % 2 == 0 ? "#1E293B" : "#243247"), BorderBrush = GameBrush("#334155"), BorderThickness = new Thickness(1) };
                    Canvas.SetLeft(tile, x * GameTileSize); Canvas.SetTop(tile, y * GameTileSize); gameCanvas.Children.Add(tile);
                    if (gameOreCells.Contains(GameCell(x, y)))
                    {
                        Border rock = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(10), Background = GameBrush("#475569"), BorderBrush = GameBrush("#64748B"), BorderThickness = new Thickness(2), Child = new TextBlock { Text = "◆", Foreground = GameBrush("#60A5FA"), FontSize = 25, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                        Canvas.SetLeft(rock, x * GameTileSize + 9); Canvas.SetTop(rock, y * GameTileSize + 9); Panel.SetZIndex(rock, 3); gameCanvas.Children.Add(rock);
                    }
                }
            }

            Canvas miner = new Canvas { Width = 46, Height = 56 };
            AddGameRect(miner, 9, 0, 28, 22, "#C58D67");
            AddGameRect(miner, 9, 0, 28, 7, "#3B261B");
            AddGameRect(miner, 14, 10, 4, 4, "#E5F3FF");
            AddGameRect(miner, 28, 10, 4, 4, "#E5F3FF");
            AddGameRect(miner, 7, 22, 32, 23, "#14B8A6");
            AddGameRect(miner, 8, 45, 14, 11, "#3157A4");
            AddGameRect(miner, 24, 45, 14, 11, "#3157A4");
            Canvas.SetLeft(miner, gamePlayerX * GameTileSize + 9); Canvas.SetTop(miner, gamePlayerY * GameTileSize + 4); Panel.SetZIndex(miner, 8); gameCanvas.Children.Add(miner);
        }

        private void AddGameRect(Canvas parent, double x, double y, double width, double height, string color)
        {
            System.Windows.Shapes.Rectangle part = new System.Windows.Shapes.Rectangle { Width = width, Height = height, Fill = GameBrush(color) };
            Canvas.SetLeft(part, x); Canvas.SetTop(part, y); parent.Children.Add(part);
        }

        private void LoadMineGame()
        {
            try
            {
                if (!File.Exists(mineGamePath)) return;
                MineGameSave save = new JavaScriptSerializer().Deserialize<MineGameSave>(File.ReadAllText(mineGamePath));
                if (save == null) return;
                gameCoins = Math.Max(0, save.Coins); gameOre = Math.Max(0, save.Ore); gameBackpackCapacity = Math.Max(20, save.BackpackCapacity); gamePickaxeLevel = Math.Max(1, save.PickaxeLevel); gameTotalMined = Math.Max(0, save.TotalMined); gameTotalSold = Math.Max(0, save.TotalSold); gameSteps = Math.Max(0, save.Steps);
                gameOre = Math.Min(gameOre, gameBackpackCapacity);
            }
            catch { }
        }

        private void SaveMineGame()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(mineGamePath));
                MineGameSave save = new MineGameSave { Coins = gameCoins, Ore = gameOre, BackpackCapacity = gameBackpackCapacity, PickaxeLevel = gamePickaxeLevel, TotalMined = gameTotalMined, TotalSold = gameTotalSold, Steps = gameSteps };
                File.WriteAllText(mineGamePath, new JavaScriptSerializer().Serialize(save));
            }
            catch { }
        }

        private void LoadHubData()
        {
            notesList.ItemsSource = hubNotes;
            tasksList.ItemsSource = hubTasks;
            financeList.ItemsSource = hubFinance;
            documentsList.ItemsSource = hubDocuments;
            try
            {
                if (File.Exists(hubDataPath))
                {
                    HubData data = new JavaScriptSerializer().Deserialize<HubData>(File.ReadAllText(hubDataPath));
                    if (data != null)
                    {
                        if (data.Notes != null) foreach (NoteItem item in data.Notes) hubNotes.Add(item);
                        if (data.Tasks != null) foreach (TaskItem item in data.Tasks) hubTasks.Add(item);
                        if (data.Finance != null) foreach (FinanceItem item in data.Finance) hubFinance.Add(item);
                        if (data.Documents != null) foreach (DocumentItem item in data.Documents) hubDocuments.Add(item);
                    }
                }
            }
            catch { }
            UpdateFinanceBalance();
            if (hubNotes.Count > 0) notesList.SelectedIndex = 0;
            if (hubDocuments.Count > 0) documentsList.SelectedIndex = 0;
        }

        private void SaveHubData()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(hubDataPath));
                HubData data = new HubData { Notes = hubNotes.ToList(), Tasks = hubTasks.ToList(), Finance = hubFinance.ToList(), Documents = hubDocuments.ToList() };
                File.WriteAllText(hubDataPath, new JavaScriptSerializer().Serialize(data));
            }
            catch { }
        }

        private void AddNote()
        {
            NoteItem note = new NoteItem { Id = Guid.NewGuid().ToString("N"), Title = "Новая заметка", Text = "", UpdatedAt = DateTime.Now };
            hubNotes.Insert(0, note); notesList.SelectedItem = note; SaveHubData(); noteTitleBox.Focus(); noteTitleBox.SelectAll();
        }

        private void SelectNote(NoteItem note)
        {
            updatingNoteEditor = true;
            selectedNote = note;
            noteTitleBox.Text = note == null ? "" : note.Title;
            noteTextBox.Text = note == null ? "" : note.Text;
            updatingNoteEditor = false;
        }

        private void AutoSaveSelectedNote()
        {
            if (updatingNoteEditor || selectedNote == null) return;
            selectedNote.Title = string.IsNullOrWhiteSpace(noteTitleBox.Text) ? "Без названия" : noteTitleBox.Text.Trim();
            selectedNote.Text = noteTextBox.Text;
            selectedNote.UpdatedAt = DateTime.Now;
            notesList.Items.Refresh();
            SaveHubData();
        }

        private void SaveSelectedNote()
        {
            if (selectedNote == null) { AddNote(); selectedNote = notesList.SelectedItem as NoteItem; }
            selectedNote.Title = string.IsNullOrWhiteSpace(noteTitleBox.Text) ? "Без названия" : noteTitleBox.Text.Trim();
            selectedNote.Text = noteTextBox.Text;
            selectedNote.UpdatedAt = DateTime.Now;
            notesList.Items.Refresh(); SaveHubData(); ShowToast("Заметка сохранена");
        }

        private void DeleteSelectedNote()
        {
            NoteItem note = notesList.SelectedItem as NoteItem;
            if (note == null) { ShowToast("Выберите заметку"); return; }
            hubNotes.Remove(note); selectedNote = null; noteTitleBox.Text = ""; noteTextBox.Text = ""; SaveHubData();
        }

        private void AddTask()
        {
            string title = taskInput.Text.Trim();
            if (title.Length == 0) { ShowToast("Введите название задачи"); return; }
            hubTasks.Insert(0, new TaskItem { Id = Guid.NewGuid().ToString("N"), Title = title, IsDone = false });
            taskInput.Text = ""; SaveHubData(); taskInput.Focus();
        }

        private void ToggleSelectedTask()
        {
            TaskItem task = tasksList.SelectedItem as TaskItem;
            if (task == null) { ShowToast("Выберите задачу"); return; }
            task.IsDone = !task.IsDone; tasksList.Items.Refresh(); SaveHubData();
        }

        private void DeleteSelectedTask()
        {
            TaskItem task = tasksList.SelectedItem as TaskItem;
            if (task == null) { ShowToast("Выберите задачу"); return; }
            hubTasks.Remove(task); SaveHubData();
        }

        private void AddFinanceOperation()
        {
            string description = financeDescriptionBox.Text.Trim();
            double amount;
            string raw = financeAmountBox.Text.Trim().Replace(',', '.');
            if (description.Length == 0 || !double.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) || amount <= 0) { ShowToast("Введите описание и корректную сумму"); return; }
            if (financeTypeBox.SelectedIndex == 1) amount = -amount;
            hubFinance.Insert(0, new FinanceItem { Id = Guid.NewGuid().ToString("N"), Description = description, Amount = amount, CreatedAt = DateTime.Now });
            financeDescriptionBox.Text = ""; financeAmountBox.Text = ""; UpdateFinanceBalance(); SaveHubData();
        }

        private void DeleteFinanceOperation()
        {
            FinanceItem item = financeList.SelectedItem as FinanceItem;
            if (item == null) { ShowToast("Выберите операцию"); return; }
            hubFinance.Remove(item); UpdateFinanceBalance(); SaveHubData();
        }

        private void UpdateFinanceBalance()
        {
            if (financeBalanceText == null) return;
            double balance = hubFinance.Sum(x => x.Amount);
            financeBalanceText.Text = balance.ToString("0.00", CultureInfo.InvariantCulture) + " ₴";
            financeBalanceText.Foreground = GameBrush(balance < 0 ? "#FB7185" : "#34D399");
            if (financeList != null) financeList.Items.Refresh();
        }

        private void AddDocument()
        {
            DocumentItem document = new DocumentItem { Id = Guid.NewGuid().ToString("N"), Title = "Новый документ", Content = "", UpdatedAt = DateTime.Now };
            hubDocuments.Insert(0, document); documentsList.SelectedItem = document; SaveHubData(); documentTitleBox.Focus(); documentTitleBox.SelectAll();
        }

        private void SelectDocument(DocumentItem document)
        {
            selectedDocument = document;
            documentTitleBox.Text = document == null ? "" : document.Title;
            documentContentBox.Text = document == null ? "" : document.Content;
        }

        private void SaveSelectedDocument()
        {
            if (selectedDocument == null) { AddDocument(); selectedDocument = documentsList.SelectedItem as DocumentItem; }
            selectedDocument.Title = string.IsNullOrWhiteSpace(documentTitleBox.Text) ? "Без названия" : documentTitleBox.Text.Trim();
            selectedDocument.Content = documentContentBox.Text;
            selectedDocument.UpdatedAt = DateTime.Now;
            documentsList.Items.Refresh(); SaveHubData(); ShowToast("Документ сохранён");
        }

        private void DeleteSelectedDocument()
        {
            DocumentItem document = documentsList.SelectedItem as DocumentItem;
            if (document == null) { ShowToast("Выберите документ"); return; }
            hubDocuments.Remove(document); selectedDocument = null; documentTitleBox.Text = ""; documentContentBox.Text = ""; SaveHubData();
        }

        private void ExportSelectedDocument()
        {
            if (selectedDocument == null) { ShowToast("Сначала выберите и сохраните документ"); return; }
            SaveSelectedDocument();
            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Текстовый документ (*.txt)|*.txt", DefaultExt = ".txt", AddExtension = true, FileName = selectedDocument.Title };
            if (dialog.ShowDialog(this) == true) { File.WriteAllText(dialog.FileName, selectedDocument.Content, Encoding.UTF8); ShowToast("Документ экспортирован"); }
        }

        private void ChooseConverterFile()
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Все файлы|*.*" };
            if (dialog.ShowDialog(this) == true) { converterSourcePath = dialog.FileName; converterSourceText.Text = Path.GetFileName(dialog.FileName); }
        }

        private void ConvertSelectedFile()
        {
            if (string.IsNullOrEmpty(converterSourcePath) || !File.Exists(converterSourcePath)) { ShowToast("Сначала выберите изображение"); return; }
            ComboBoxItem selected = converterFormatBox.SelectedItem as ComboBoxItem;
            string format = selected == null || selected.Tag == null ? "png" : selected.Tag.ToString();
            string filter = format == "jpg" ? "JPEG (*.jpg)|*.jpg" : format == "bmp" ? "BMP (*.bmp)|*.bmp" : format == "gif" ? "GIF (*.gif)|*.gif" : format == "tiff" ? "TIFF (*.tiff)|*.tiff" : "PNG (*.png)|*.png";
            Microsoft.Win32.SaveFileDialog save = new Microsoft.Win32.SaveFileDialog { AddExtension = true, DefaultExt = "." + format, FileName = Path.GetFileNameWithoutExtension(converterSourcePath) + "-converted", Filter = filter };
            if (save.ShowDialog(this) != true) return;
            try
            {
                BitmapFrame frame;
                using (FileStream input = File.OpenRead(converterSourcePath))
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    frame = decoder.Frames[0];
                }
                BitmapEncoder encoder = format == "jpg" ? (BitmapEncoder)new JpegBitmapEncoder() : format == "bmp" ? (BitmapEncoder)new BmpBitmapEncoder() : format == "gif" ? (BitmapEncoder)new GifBitmapEncoder() : format == "tiff" ? (BitmapEncoder)new TiffBitmapEncoder() : new PngBitmapEncoder();
                encoder.Frames.Add(frame);
                using (FileStream output = File.Create(save.FileName)) encoder.Save(output);
                ShowToast("Изображение успешно конвертировано");
            }
            catch (Exception ex) { ShowToast("Не удалось конвертировать: " + ex.Message); }
        }

        private void WireCalculatorButtons()
        {
            string[] names = new string[] { "CalcClear", "CalcBack", "CalcDivide", "CalcMultiply", "Calc7", "Calc8", "Calc9", "CalcMinus", "Calc4", "Calc5", "Calc6", "CalcPlus", "Calc1", "Calc2", "Calc3", "CalcEquals", "CalcSign", "Calc0", "Calc00", "CalcDot" };
            foreach (string name in names) Find<Button>(name).Click += CalculatorButton_Click;
        }

        private bool HandleCalculatorKey(KeyEventArgs e)
        {
            string key = null;
            if (e.Key >= Key.D0 && e.Key <= Key.D9) key = ((int)e.Key - (int)Key.D0).ToString(CultureInfo.InvariantCulture);
            else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) key = ((int)e.Key - (int)Key.NumPad0).ToString(CultureInfo.InvariantCulture);
            else if (e.Key == Key.Add || (e.Key == Key.OemPlus && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)) key = "+";
            else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) key = "−";
            else if (e.Key == Key.Multiply) key = "×";
            else if (e.Key == Key.Divide || e.Key == Key.OemQuestion) key = "÷";
            else if (e.Key == Key.Decimal || e.Key == Key.OemPeriod || e.Key == Key.OemComma) key = ".";
            else if (e.Key == Key.Enter || e.Key == Key.Return) key = "=";
            else if (e.Key == Key.Back) key = "⌫";
            else if (e.Key == Key.Delete || e.Key == Key.Escape) key = "C";
            if (key == null) return false;
            ProcessCalculatorKey(key);
            return true;
        }

        private void CalculatorButton_Click(object sender, RoutedEventArgs e)
        {
            string key = Convert.ToString(((Button)sender).Content, CultureInfo.InvariantCulture);
            ProcessCalculatorKey(key);
        }

        private void ProcessCalculatorKey(string key)
        {
            if (key == "C") { calculatorDisplay.Text = "0"; calculatorStored = 0; calculatorOperation = null; calculatorNewNumber = true; return; }
            if (key == "⌫")
            {
                if (!calculatorNewNumber && calculatorDisplay.Text.Length > 1)
                {
                    calculatorDisplay.Text = calculatorDisplay.Text.Substring(0, calculatorDisplay.Text.Length - 1);
                    if (calculatorDisplay.Text == "-" || calculatorDisplay.Text == "") calculatorDisplay.Text = "0";
                }
                else calculatorDisplay.Text = "0";
                return;
            }
            if (key == "±") { double value = CalculatorValue(); calculatorDisplay.Text = FormatCalculatorResult(-value); return; }
            if (key == "+" || key == "−" || key == "×" || key == "÷") { if (calculatorOperation != null && !calculatorNewNumber) ApplyCalculatorOperation(); calculatorStored = CalculatorValue(); calculatorOperation = key; calculatorNewNumber = true; return; }
            if (key == "=") { ApplyCalculatorOperation(); return; }
            if (calculatorDisplay.Text.Length >= 18 && key != ".") return;
            if (calculatorNewNumber || calculatorDisplay.Text == "Ошибка") { calculatorDisplay.Text = key == "." ? "0." : key; calculatorNewNumber = false; }
            else if (key == ".") { if (!calculatorDisplay.Text.Contains(".")) calculatorDisplay.Text += "."; }
            else if (calculatorDisplay.Text == "0") calculatorDisplay.Text = key;
            else calculatorDisplay.Text += key;
        }

        private double CalculatorValue()
        {
            double value;
            return double.TryParse(calculatorDisplay.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private void ApplyCalculatorOperation()
        {
            if (calculatorOperation == null) return;
            double current = CalculatorValue();
            double result = calculatorStored;
            if (calculatorOperation == "+") result += current;
            else if (calculatorOperation == "−") result -= current;
            else if (calculatorOperation == "×") result *= current;
            else if (calculatorOperation == "÷")
            {
                if (Math.Abs(current) < 0.0000000001) { calculatorDisplay.Text = "Ошибка"; calculatorOperation = null; calculatorNewNumber = true; return; }
                result /= current;
            }
            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                calculatorDisplay.Text = "Ошибка";
                calculatorStored = 0; calculatorOperation = null; calculatorNewNumber = true;
                return;
            }
            calculatorDisplay.Text = FormatCalculatorResult(result);
            calculatorStored = result; calculatorOperation = null; calculatorNewNumber = true;
        }

        private static string FormatCalculatorResult(double value)
        {
            return value.ToString("0.############", CultureInfo.InvariantCulture);
        }

        private void LoadTheme()
        {
            string theme = "white";
            try { if (File.Exists(settingsPath)) theme = File.ReadAllText(settingsPath).Trim().ToLowerInvariant(); } catch { }
            int index = theme == "dark" ? 1 : theme == "gray" ? 2 : theme == "green" ? 3 : 0;
            themeSelector.SelectedIndex = index;
            ApplySelectedTheme(false);
        }

        private void ApplySelectedTheme(bool save)
        {
            ComboBoxItem selected = themeSelector.SelectedItem as ComboBoxItem;
            string theme = selected == null || selected.Tag == null ? "white" : selected.Tag.ToString();
            string ink, muted, line, canvas, surface, title, sidebar, field, fieldBorder, hover, selectedColor, softAccent, softPanel, accentOne, accentTwo;
            if (theme == "dark")
            {
                ink = "#F4F5FA"; muted = "#9EA3B5"; line = "#303543"; canvas = "#11131A"; surface = "#1B1E28"; title = "#171A23"; sidebar = "#090B10"; field = "#252936"; fieldBorder = "#343A49"; hover = "#29273A"; selectedColor = "#37304F"; softAccent = "#332B4D"; softPanel = "#242332"; accentOne = "#8B5CF6"; accentTwo = "#EC4899";
            }
            else if (theme == "gray")
            {
                ink = "#23262C"; muted = "#676C76"; line = "#C3C7CE"; canvas = "#D6D9DE"; surface = "#ECEEF1"; title = "#E2E4E8"; sidebar = "#343840"; field = "#E0E2E6"; fieldBorder = "#C8CBD1"; hover = "#E0DEE5"; selectedColor = "#D5D0DC"; softAccent = "#DDD8E5"; softPanel = "#DFE1E5"; accentOne = "#596579"; accentTwo = "#8E667F";
            }
            else if (theme == "green")
            {
                ink = "#EDF8F2"; muted = "#9EB7AA"; line = "#70446B58"; canvas = "#E014261F"; surface = "#D51B3027"; title = "#EA12211C"; sidebar = "#EE091711"; field = "#CF253E33"; fieldBorder = "#8A4D7663"; hover = "#9A29483A"; selectedColor = "#BC365F4B"; softAccent = "#A3244A3A"; softPanel = "#A8203931"; accentOne = "#22C55E"; accentTwo = "#14B8A6";
            }
            else
            {
                theme = "white"; ink = "#18192A"; muted = "#74778A"; line = "#D9DBE8"; canvas = "#F4F5FA"; surface = "#FFFFFF"; title = "#F9F9FC"; sidebar = "#191A2D"; field = "#F7F7FB"; fieldBorder = "#E7E7F0"; hover = "#F6F4FF"; selectedColor = "#EEEAFE"; softAccent = "#F0EDFF"; softPanel = "#F7F5FF"; accentOne = "#8B5CF6"; accentTwo = "#EC4899";
            }

            SetBrush("Ink", ink); SetBrush("Muted", muted); SetBrush("Line", line); SetBrush("CanvasBrush", canvas); SetBrush("SurfaceBrush", surface); SetBrush("TitleBrush", title); SetBrush("SidebarBrush", sidebar); SetBrush("FieldBrush", field); SetBrush("FieldBorderBrush", fieldBorder); SetBrush("HoverBrush", hover); SetBrush("SelectedBrush", selectedColor); SetBrush("SoftAccentBrush", softAccent); SetBrush("SoftPanelBrush", softPanel);
            SetBrush("ScrollThumbBrush", theme == "green" ? "#7022C55E" : theme == "gray" ? "#70596579" : theme == "dark" ? "#708B5CF6" : "#608B5CF6");
            SetBrush("ScrollThumbHoverBrush", theme == "green" ? "#E022C55E" : theme == "gray" ? "#D0596579" : "#D08B5CF6");
            SetBrush("GhostBrush", theme == "green" ? "#A8244A3A" : theme == "gray" ? "#D7D9DF" : theme == "dark" ? "#3A3152" : "#F0EEFF");
            SetBrush("GhostTextBrush", theme == "green" ? "#74E5B8" : theme == "gray" ? "#4F596B" : theme == "dark" ? "#C4B5FD" : "#4F46E5");
            SetBrush("DangerBrush", theme == "green" ? "#7A4A252D" : theme == "dark" ? "#59303C" : theme == "gray" ? "#E7D7DC" : "#FFF0F3");
            SetBrush("DangerTextBrush", theme == "green" ? "#FF9CAA" : theme == "dark" ? "#FF9BAF" : "#DC365B");
            SetBrush("IconHoverBrush", theme == "green" ? "#8A29483A" : theme == "gray" ? "#D6D8DE" : theme == "dark" ? "#302D42" : "#EEEFFA");
            LinearGradientBrush accent = new LinearGradientBrush();
            accent.StartPoint = new Point(0, 0); accent.EndPoint = new Point(1, 1);
            accent.GradientStops.Add(new GradientStop(ToColor(accentOne), 0)); accent.GradientStops.Add(new GradientStop(ToColor(accentTwo), 1));
            Resources["AccentBrush"] = accent;
            Icon = CreateThemeIcon(accentOne, accentTwo);
            Find<Ellipse>("OrbOne").Fill = GlowBrush(theme == "green" ? "#4822C55E" : "#3C8B5CF6");
            Find<Ellipse>("OrbTwo").Fill = GlowBrush(theme == "green" ? "#4214B8A6" : "#38EC4899");
            Find<Ellipse>("OrbThree").Fill = GlowBrush(theme == "gray" ? "#35596579" : "#3522CFA5");
            if (save)
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)); File.WriteAllText(settingsPath, theme); } catch { }
                if (mainView.Visibility == Visibility.Visible) { mainView.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 1, TimeSpan.FromMilliseconds(360))); ShowToast("Тема изменена"); }
            }
        }

        private void SetBrush(string key, string color) { Resources[key] = new SolidColorBrush(ToColor(color)); }
        private static Color ToColor(string value) { return (Color)ColorConverter.ConvertFromString(value); }
        private static Brush GlowBrush(string value)
        {
            Color center = ToColor(value);
            Color middle = Color.FromArgb((byte)(center.A * .42), center.R, center.G, center.B);
            Color edge = Color.FromArgb(0, center.R, center.G, center.B);
            RadialGradientBrush brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(center, 0)); brush.GradientStops.Add(new GradientStop(middle, .55)); brush.GradientStops.Add(new GradientStop(edge, 1));
            return brush;
        }

        private static ImageSource CreateThemeIcon(string first, string second)
        {
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext drawing = visual.RenderOpen())
            {
                LinearGradientBrush background = new LinearGradientBrush(ToColor(first), ToColor(second), new Point(0, 0), new Point(1, 1));
                drawing.DrawRoundedRectangle(background, null, new Rect(2, 2, 60, 60), 15, 15);
                FormattedText letter = new FormattedText("V", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 35, Brushes.White, 1.0);
                drawing.DrawText(letter, new Point((64 - letter.Width) / 2, (64 - letter.Height) / 2 - 2));
            }
            RenderTargetBitmap bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze();
            return bitmap;
        }

        private void ShowLogin()
        {
            bool exists = File.Exists(vaultPath);
            loginTitle.Text = exists ? "С возвращением" : "Создайте хранилище";
            loginSubtitle.Text = exists ? "Введите мастер-пароль, чтобы расшифровать данные" : "Придумайте мастер-пароль. Его невозможно восстановить — сохраните его в надёжном месте.";
            confirmPanel.Visibility = exists ? Visibility.Collapsed : Visibility.Visible;
            loginButton.Content = exists ? "Открыть хранилище" : "Создать хранилище";
            loginError.Text = "";
            loginPassword.Password = "";
            confirmPassword.Password = "";
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string entered = loginPassword.Password;
            if (entered.Length < 10) { loginError.Text = "Мастер-пароль должен содержать минимум 10 символов."; Shake(loginButton); return; }
            bool exists = File.Exists(vaultPath);
            if (!exists && entered != confirmPassword.Password) { loginError.Text = "Пароли не совпадают."; Shake(loginButton); return; }
            SetLoginBusy(true, exists);
            try
            {
                VaultOpenResult opened;
                if (exists)
                    opened = await Task.Run(delegate { return VaultCrypto.Open(vaultPath, entered); });
                else
                {
                    opened = await Task.Run(delegate
                    {
                        VaultOpenResult created = VaultCrypto.CreateSession(entered);
                        VaultCrypto.SaveWithKey(vaultPath, created.Salt, created.KeyMaterial, new List<VaultEntry>());
                        return created;
                    });
                }
                entries.Clear();
                foreach (VaultEntry item in opened.Entries) entries.Add(item);
                sessionSalt = opened.Salt;
                sessionKey = opened.KeyMaterial;
                loginPassword.Password = ""; confirmPassword.Password = "";
                OpenVault();
            }
            catch (Exception ex)
            {
                loginError.Text = ex is CryptographicException ? "Неверный мастер-пароль." : "Не удалось открыть хранилище: " + ex.Message;
                Shake(loginButton);
            }
            finally { SetLoginBusy(false, exists); entered = null; }
        }

        private void SetLoginBusy(bool busy, bool existingVault)
        {
            loginButton.IsEnabled = !busy;
            loginPassword.IsEnabled = !busy;
            confirmPassword.IsEnabled = !busy;
            loginButton.Content = busy ? (existingVault ? "Расшифровываю…" : "Создаю защищённый сейф…") : (existingVault ? "Открыть хранилище" : "Создать хранилище");
            loginProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            TranslateTransform move = loginProgressGlow.RenderTransform as TranslateTransform;
            if (move == null) { move = new TranslateTransform(-110, 0); loginProgressGlow.RenderTransform = move; }
            if (busy)
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-110, 350, TimeSpan.FromMilliseconds(720)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            else
                move.BeginAnimation(TranslateTransform.XProperty, null);
        }

        private void OpenVault()
        {
            entriesView = CollectionViewSource.GetDefaultView(entries);
            entriesView.Filter = delegate(object item)
            {
                VaultEntry entry = item as VaultEntry;
                string q = (searchBox.Text ?? "").Trim().ToLowerInvariant();
                if (q.Length == 0) return true;
                return Contains(entry.Title, q) || Contains(entry.Username, q) || Contains(entry.Website, q);
            };
            entriesList.ItemsSource = entriesView;
            loginView.Visibility = Visibility.Collapsed;
            mainView.Visibility = Visibility.Visible;
            Fade(mainView);
            AnimateDashboardEntrance();
            StartAmbientAnimations();
            UpdateStats();
            SelectEntry(null);
            lastActivity = DateTime.Now;
        }

        private static bool Contains(string text, string query) { return !string.IsNullOrEmpty(text) && text.ToLowerInvariant().Contains(query); }

        private void LockVault(string message)
        {
            if (mainView.Visibility != Visibility.Visible) return;
            Clipboard.Clear();
            entries.Clear();
            if (sessionKey != null) Array.Clear(sessionKey, 0, sessionKey.Length);
            if (sessionSalt != null) Array.Clear(sessionSalt, 0, sessionSalt.Length);
            sessionKey = null; sessionSalt = null;
            selectedEntry = null;
            mainView.Visibility = Visibility.Collapsed;
            loginView.Visibility = Visibility.Visible;
            ShowLogin();
            Fade(loginView);
            loginSubtitle.Text = message + ". Введите мастер-пароль.";
            loginPassword.Focus();
        }

        private void AddEntry()
        {
            VaultEntry item = new VaultEntry { Id = Guid.NewGuid().ToString("N"), Title = "Новая запись", Username = "", Password = "", Website = "", Notes = "", Favorite = false, UpdatedAt = DateTime.Now };
            entries.Insert(0, item);
            entriesList.SelectedItem = item;
            entriesList.ScrollIntoView(item);
            UpdateStats();
            titleBox.Focus(); titleBox.SelectAll();
        }

        private void SelectEntry(VaultEntry entry)
        {
            selectedEntry = entry;
            bool has = entry != null;
            detailEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            detailEditor.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            if (!has) return;
            titleBox.Text = entry.Title ?? "";
            usernameBox.Text = entry.Username ?? "";
            SetPassword(entry.Password ?? "");
            websiteBox.Text = entry.Website ?? "";
            notesBox.Text = entry.Notes ?? "";
            favoriteBox.IsChecked = entry.Favorite;
            updatedLabel.Text = entry.UpdatedLabel;
            RefreshPasswordHistory();
            detailEditor.ScrollToTop();
            Fade(detailEditor);
        }

        private void SaveSelected()
        {
            if (selectedEntry == null) return;
            if (string.IsNullOrWhiteSpace(titleBox.Text)) { ShowToast("Добавьте название записи"); titleBox.Focus(); return; }
            string newPassword = GetPassword();
            string oldPassword = selectedEntry.Password ?? "";
            if (newPassword != oldPassword && !string.IsNullOrEmpty(oldPassword))
            {
                if (selectedEntry.PasswordHistory == null) selectedEntry.PasswordHistory = new List<PasswordHistoryItem>();
                selectedEntry.PasswordHistory.RemoveAll(delegate(PasswordHistoryItem item) { return item == null || item.Password == oldPassword || item.Password == newPassword; });
                selectedEntry.PasswordHistory.Insert(0, new PasswordHistoryItem { Password = oldPassword, ChangedAt = DateTime.Now });
                while (selectedEntry.PasswordHistory.Count > 10) selectedEntry.PasswordHistory.RemoveAt(selectedEntry.PasswordHistory.Count - 1);
            }
            selectedEntry.Title = titleBox.Text.Trim();
            selectedEntry.Username = usernameBox.Text.Trim();
            selectedEntry.Password = newPassword;
            selectedEntry.Website = websiteBox.Text.Trim();
            selectedEntry.Notes = notesBox.Text;
            selectedEntry.Favorite = favoriteBox.IsChecked == true;
            selectedEntry.UpdatedAt = DateTime.Now;
            SaveVault();
            entriesView.Refresh();
            updatedLabel.Text = selectedEntry.UpdatedLabel;
            RefreshPasswordHistory();
            UpdateStats();
            ShowToast("Изменения зашифрованы и сохранены");
        }

        private void DeleteSelected()
        {
            if (selectedEntry == null) return;
            VaultEntry target = selectedEntry;
            ShowDialog("Удалить запись?", "«" + target.Title + "» будет удалена из зашифрованного хранилища. Отменить это действие после подтверждения нельзя.", "Удалить", true, true, delegate
            {
                entries.Remove(target);
                if (selectedEntry == target) selectedEntry = null;
                SaveVault();
                SelectEntry(null);
                UpdateStats();
                ShowToast("Запись удалена");
            });
        }

        private void SaveVault()
        {
            try { VaultCrypto.SaveWithKey(vaultPath, sessionSalt, sessionKey, entries); }
            catch (Exception ex) { ShowDialog("Не удалось сохранить", ex.Message, "Понятно", false, false, null); }
        }

        private void ShowDialog(string title, string message, string confirmText, bool destructive, bool showCancel, Action action)
        {
            dialogTitle.Text = title;
            dialogMessage.Text = message;
            dialogConfirmButton.Content = confirmText;
            dialogConfirmButton.Style = (Style)Resources[destructive ? "DangerButton" : "ActionButton"];
            dialogCancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(dialogConfirmButton, showCancel ? 2 : 0);
            Grid.SetColumnSpan(dialogConfirmButton, showCancel ? 1 : 3);
            dialogAction = action;
            dialogOverlay.Visibility = Visibility.Visible;
            dialogOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            ScaleTransform scale = dialogCard.RenderTransform as ScaleTransform;
            if (scale == null) { scale = new ScaleTransform(.9, .9); dialogCard.RenderTransform = scale; }
            CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.88, 1, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.88, 1, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease });
            dialogConfirmButton.Focus();
        }

        private void HideDialog()
        {
            if (dialogOverlay.Visibility != Visibility.Visible) return;
            dialogAction = null;
            DoubleAnimation fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            fade.Completed += delegate { dialogOverlay.Visibility = Visibility.Collapsed; };
            dialogOverlay.BeginAnimation(OpacityProperty, fade);
            ScaleTransform scale = dialogCard.RenderTransform as ScaleTransform;
            if (scale != null)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.96, TimeSpan.FromMilliseconds(140)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.96, TimeSpan.FromMilliseconds(140)));
            }
        }

        private void CloseDialogImmediately()
        {
            dialogAction = null;
            dialogOverlay.BeginAnimation(OpacityProperty, null);
            dialogOverlay.Opacity = 0;
            dialogOverlay.Visibility = Visibility.Collapsed;
        }

        private void RefreshPasswordHistory()
        {
            if (selectedEntry == null)
            {
                historyPanel.Visibility = Visibility.Collapsed;
                passwordHistoryBox.ItemsSource = null;
                return;
            }
            if (selectedEntry.PasswordHistory == null) selectedEntry.PasswordHistory = new List<PasswordHistoryItem>();
            List<PasswordHistoryItem> history = selectedEntry.PasswordHistory.Where(x => x != null && !string.IsNullOrEmpty(x.Password)).OrderByDescending(x => x.ChangedAt).Take(10).ToList();
            historyPanel.Visibility = history.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            passwordHistoryBox.ItemsSource = history;
            historyCount.Text = history.Count.ToString() + " из 10";
            if (history.Count > 0) passwordHistoryBox.SelectedIndex = 0;
        }

        private void UpdateStats()
        {
            totalCount.Text = entries.Count.ToString();
            favoriteCount.Text = entries.Count(x => x.Favorite).ToString();
            emptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            int strong = 0, medium = 0, weak = 0;
            foreach (VaultEntry entry in entries)
            {
                int score = PasswordStrengthScore(entry.Password ?? "");
                if (score >= 5) strong++; else if (score >= 3) medium++; else weak++;
            }
            strongCount.Text = strong.ToString(); mediumCount.Text = medium.ToString(); weakCount.Text = weak.ToString();
            int total = entries.Count;
            int health = total == 0 ? 0 : (int)Math.Round((strong * 100.0 + medium * 60.0 + weak * 25.0) / total);
            healthScore.Text = total == 0 ? "—" : health.ToString() + "%";
            AnimateWidth(strongBar, total == 0 ? 0 : 150.0 * strong / total);
            AnimateWidth(mediumBar, total == 0 ? 0 : 150.0 * medium / total);
            AnimateWidth(weakBar, total == 0 ? 0 : 150.0 * weak / total);
        }

        private string GetPassword() { return showPassword.IsChecked == true ? visiblePasswordBox.Text : passwordBox.Password; }
        private void SetPassword(string value) { passwordBox.Password = value ?? ""; visiblePasswordBox.Text = value ?? ""; UpdateStrength(value ?? ""); }

        private string GeneratePassword()
        {
            List<string> groups = new List<string>();
            if (lowerCheck.IsChecked == true) groups.Add("abcdefghijkmnopqrstuvwxyz");
            if (upperCheck.IsChecked == true) groups.Add("ABCDEFGHJKLMNPQRSTUVWXYZ");
            if (digitsCheck.IsChecked == true) groups.Add("23456789");
            if (symbolsCheck.IsChecked == true) groups.Add("!@#$%^&*()-_=+[]{}?");
            if (groups.Count == 0) { lowerCheck.IsChecked = true; groups.Add("abcdefghijkmnopqrstuvwxyz"); }
            string chars = string.Concat(groups.ToArray());
            int length = (int)lengthSlider.Value;
            List<char> result = new List<char>(length);
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                foreach (string group in groups) result.Add(group[NextIndex(rng, group.Length)]);
                while (result.Count < length) result.Add(chars[NextIndex(rng, chars.Length)]);
                for (int i = result.Count - 1; i > 0; i--)
                {
                    int j = NextIndex(rng, i + 1);
                    char swap = result[i]; result[i] = result[j]; result[j] = swap;
                }
            }
            return new string(result.ToArray());
        }

        private static int NextIndex(RandomNumberGenerator rng, int maximum)
        {
            byte[] sample = new byte[4];
            uint range = (uint)maximum;
            uint cutoff = UInt32.MaxValue - (UInt32.MaxValue % range);
            while (true)
            {
                rng.GetBytes(sample);
                uint value = BitConverter.ToUInt32(sample, 0);
                if (value < cutoff) return (int)(value % range);
            }
        }

        private void UpdateStrength(string password)
        {
            int score = PasswordStrengthScore(password);
            string[] labels = { "Нет пароля", "Слабый", "Средний", "Хороший", "Сильный", "Очень сильный" };
            string[] colors = { "#D7D8E1", "#EF476F", "#F59E0B", "#D1A21A", "#28B487", "#7557E8" };
            strengthText.Text = labels[Math.Min(score, 5)];
            strengthBar.Background = (Brush)new BrushConverter().ConvertFromString(colors[Math.Min(score, 5)]);
            double width = score == 0 ? 0 : Math.Min(300, score * 60);
            strengthBar.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(250)));
        }

        private static int PasswordStrengthScore(string password)
        {
            if (string.IsNullOrEmpty(password)) return 0;
            int score = 0;
            if (password.Length >= 10) score++;
            if (password.Length >= 16) score++;
            if (password.Any(char.IsLower) && password.Any(char.IsUpper)) score++;
            if (password.Any(char.IsDigit)) score++;
            if (password.Any(c => !char.IsLetterOrDigit(c))) score++;
            return score;
        }

        private static void AnimateWidth(FrameworkElement element, double width)
        {
            DoubleAnimation animation = new DoubleAnimation { To = width, Duration = TimeSpan.FromMilliseconds(480), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            element.BeginAnimation(WidthProperty, animation);
        }

        private void AnimateDashboardEntrance()
        {
            TranslateTransform sideMove = new TranslateTransform(-34, 0);
            sidebarPanel.RenderTransform = sideMove;
            sideMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-34, 0, TimeSpan.FromMilliseconds(430)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            sidebarPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360)));
            TranslateTransform contentMove = new TranslateTransform(28, 0);
            workspacePanel.RenderTransform = contentMove;
            contentMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(70), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            workspacePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(70) });
        }

        private void StartAmbientAnimations()
        {
            if (ambientStarted) return;
            ambientStarted = true;
            AnimateOrb(Find<Ellipse>("OrbOne"), -15, 34, -8, 28, 8.5, 0);
            AnimateOrb(Find<Ellipse>("OrbTwo"), 20, -32, 15, -24, 10.5, 1.2);
            AnimateOrb(Find<Ellipse>("OrbThree"), -24, 28, 10, -20, 12, 2.2);
            ambientCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(.18, .62, TimeSpan.FromSeconds(2.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }

        private static void AnimateOrb(Ellipse orb, double fromX, double toX, double fromY, double toY, double seconds, double delay)
        {
            TranslateTransform move = orb.RenderTransform as TranslateTransform;
            if (move == null) { move = new TranslateTransform(); orb.RenderTransform = move; }
            IEasingFunction ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, toX, TimeSpan.FromSeconds(seconds)) { BeginTime = TimeSpan.FromSeconds(delay), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, toY, TimeSpan.FromSeconds(seconds * 1.15)) { BeginTime = TimeSpan.FromSeconds(delay), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        }

        private void CopySecure(string value, string message)
        {
            if (string.IsNullOrEmpty(value)) { ShowToast("Здесь пока нечего копировать"); return; }
            Clipboard.SetText(value);
            ShowToast(message);
            string copied = value;
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timer.Tick += delegate
            {
                timer.Stop();
                try { if (Clipboard.ContainsText() && Clipboard.GetText() == copied) Clipboard.Clear(); } catch { }
            };
            timer.Start();
        }

        private void ShowToast(string message)
        {
            toastText.Text = message;
            Storyboard board = new Storyboard();
            DoubleAnimation animation = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(180), AutoReverse = true, BeginTime = TimeSpan.Zero };
            animation.AutoReverse = false;
            DoubleAnimation fadeOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(350), BeginTime = TimeSpan.FromSeconds(2.1) };
            Storyboard.SetTarget(animation, toast); Storyboard.SetTargetProperty(animation, new PropertyPath(OpacityProperty));
            Storyboard.SetTarget(fadeOut, toast); Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
            board.Children.Add(animation); board.Children.Add(fadeOut); board.Begin();
        }

        private static void Fade(UIElement element) { element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))); }

        private static void Shake(UIElement element)
        {
            TranslateTransform transform = new TranslateTransform(); element.RenderTransform = transform;
            DoubleAnimationUsingKeyFrames animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(-9, KeyTime.FromPercent(.2)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(8, KeyTime.FromPercent(.4)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(-6, KeyTime.FromPercent(.6)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromPercent(.8)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveMineGame();
            SaveHubData();
            if (sessionKey != null) Array.Clear(sessionKey, 0, sessionKey.Length);
            if (sessionSalt != null) Array.Clear(sessionSalt, 0, sessionSalt.Length);
            base.OnClosed(e);
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.Run(new MainWindow());
        }
    }
}