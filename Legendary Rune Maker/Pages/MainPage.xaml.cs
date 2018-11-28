﻿using LCU.NET;
using Legendary_Rune_Maker.Data;
using Legendary_Rune_Maker.Data.Providers;
using Legendary_Rune_Maker.Game;
using Legendary_Rune_Maker.Locale;
using Legendary_Rune_Maker.Utils;
using Notifications.Wpf;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace Legendary_Rune_Maker.Pages
{
    /// <summary>
    /// Interaction logic for MainPage.xaml
    /// </summary>
    public partial class MainPage : Page, IMainWindow, IPage, INotifyPropertyChanged
    {
        public static double BaseWidth { get; } = 303;
        public static double BaseHeight { get; } = 310;
        public static double ExpandWidth { get; } = 810;
        public static double ExpandHeight { get; } = 325;

        public bool Attached
        {
            get => (bool)GetValue(AttachedProperty);
            set => SetValue(AttachedProperty, value);
        }
        public static readonly DependencyProperty AttachedProperty = DependencyProperty.Register("Attached", typeof(bool), typeof(MainWindow), new PropertyMetadata(true, async (sender, e) =>
        {
            if ((bool)e.NewValue)
            {
                var p = (MainPage)sender;

                await p.ChampSelectDetector.ForceUpdate();
                await p.LoginDetector.ForceUpdate();
            }
        }));

        public bool Expanded
        {
            get { return (bool)GetValue(ExpandedProperty); }
            set { SetValue(ExpandedProperty, value); }
        }
        public static readonly DependencyProperty ExpandedProperty = DependencyProperty.Register("Expanded", typeof(bool), typeof(MainWindow));


        private Rune[] SelectedRunes => Tree.SelectedPrimary
            .Concat(Tree.SelectedSecondary).Where(o => o != null)
            .Concat(Tree.SelectedStats.Where(o => o != default).Select(o => new Rune { ID = o.ID }))
            .ToArray();

        public RunePage Page => new RunePage(SelectedRunes.Select(o => o.ID).ToArray(), Tree.PrimaryTree.ID, Tree.SecondaryTree.ID, SelectedChampion, SelectedPosition);

        private int _SelectedChampion;
        public int SelectedChampion
        {
            get => _SelectedChampion;
            private set
            {
                _SelectedChampion = value;

                Load.IsEnabled = value > 0;
            }
        }

        public Position SelectedPosition { get => PositionPicker.Selected; set => PositionPicker.SetSelectedRaw(value); }

        public bool ValidPage => SelectedRunes?.Length == 9 && SelectedChampion != 0;

        private readonly Actuator Actuator;
        private readonly ILoL LoL;
        private readonly ChampSelectDetector ChampSelectDetector;
        private readonly LoginDetector LoginDetector;

        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow Owner { get; set; }

        public MainPage(ILoL lol, ChampSelectDetector champSelectDetector, LoginDetector loginDetector,
            ReadyCheckDetector readyCheckDetector, MainWindow owner)
        {
            this.LoL = lol;
            this.ChampSelectDetector = champSelectDetector;
            this.LoginDetector = loginDetector;
            this.Actuator = new Actuator(lol, champSelectDetector, loginDetector, readyCheckDetector)
            {
                Main = this
            };

            this.Owner = owner;

            InitializeComponent();

            Version.Text = "Version " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
#if DEBUG
            Version.Text += "-debug";
#endif
            Version.Text += " by pipe01";

            this.Width = 810;
            this.DataContext = this;
        }

        public Size GetSize() => new Size(Expanded ? ExpandWidth : BaseWidth, Expanded ? ExpandHeight : BaseHeight);
        
        public void SafeInvoke(Action act)
        {
            if (!Dispatcher.CheckAccess())
                Dispatcher.Invoke(act);
            else
                act();
        }

        public T SafeInvoke<T>(Func<T> act) => !Dispatcher.CheckAccess() ? Dispatcher.Invoke(act) : act();

        public void SetState(GameStates state)
        {
            AttachChk.IsEnabled = GameState.CanUpload;

            switch (state)
            {
                case GameStates.Disconnected:
                    Status.Foreground = new SolidColorBrush(Colors.Red);
                    Status.Text = Text.Disconnected;
                    break;
                case GameStates.NotLoggedIn:
                    Status.Foreground = new SolidColorBrush(Colors.Orange);
                    Status.Text = Text.NotLoggedIn;
                    break;
                case GameStates.LoggedIn:
                    Status.Foreground = new SolidColorBrush(Colors.Green);
                    Status.Text = Text.LoggedIn;
                    break;
                case GameStates.InChampSelect:
                    Status.Foreground = new SolidColorBrush(Colors.SlateBlue);
                    Status.Text = Text.InChampSelect;
                    break;
                case GameStates.LockedIn:
                    Status.Foreground = new SolidColorBrush(Colors.YellowGreen);
                    Status.Text = Text.LockedIn;
                    break;
            }
        }

        public async Task LoadPageFromDefaultProvider()
        {
            var provider = Actuator.RuneProviders.FirstOrDefault(o => o.Name == Config.Default.LockLoadProvider)
                            ?? Actuator.RuneProviders[0];
            var positions = await provider.GetPossibleRoles(SelectedChampion);

            var position = positions.Contains(SelectedPosition) ? SelectedPosition : Position.Fill;

            var page = await provider.GetRunePage(SelectedChampion, position);

            Tree.SetPage(page);

            var champName = Riot.GetChampion(SelectedChampion).Name;

            MainWindow.ShowNotification(
                Text.PageChampInPosNotSet.FormatStr(champName, SelectedPosition.ToString().ToLower()),
                Text.PageNotSetDownloaded.FormatStr(provider.Name),
                NotificationType.Information);
        }

        private async void Upload_Click(object sender, EventArgs e)
        {
            await Page.UploadToClient(LoL.Perks);
        }

        private void Tree_SelectedRunesChanged(object sender, EventArgs e)
        {
            Upload.IsEnabled = ValidPage && GameState.CanUpload;

            if (ValidPage)
                SaveRunePageToBook();
        }

        private async Task SetChampionIndex(int index)
        {
            await SetChampion((await Riot.GetChampions())[index]);
        }

        public Task SetChampion(int championId) => SetChampion(Riot.GetChampion(championId));

        public async Task SetChampion(Champion champ)
        {
            if (champ == null)
            {
                SelectedChampion = 0;
                ChampionImage.Source = (ImageSource)Application.Current.FindResource("NoChamp");

                Tree.Clear();
                return;
            }

            SelectedChampion = champ.ID;
            ChampionImage.Source = await ImageCache.Instance.Get(champ.ImageURL);

            UpdateRunePageFromRuneBook();
        }

        private void Clear_Click(object sender, EventArgs e)
        {
            Tree.Clear();

            RuneBook.Instance.Remove(SelectedChampion, SelectedPosition);
        }

        private void UpdateRunePageFromRuneBook(bool canCopy = true)
        {
            if (canCopy && ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift))
            {
                SaveRunePageToBook();
                return;
            }

            var page = RuneBook.Instance.Get(SelectedChampion, SelectedPosition, false);

            if (page != null)
            {
                Tree.SetPage(page);
            }
            else
            {
                Tree.Clear();
            }
        }

        private void SaveRunePageToBook()
        {
            RuneBook.Instance.Remove(SelectedChampion, SelectedPosition);

            if (SelectedChampion != 0)
                RuneBook.Instance.Add(this.Page);
        }

        private void Load_Click(object sender, EventArgs e)
        {
            if (SelectedChampion == 0)
                return;

            var menu = new ContextMenu();
            Load.ContextMenu = menu;
            menu.IsOpen = true;
            
            foreach (var provider in Actuator.RuneProviders.Where(o => o.ProviderOptions.HasFlag(Provider.Options.RunePages)))
            {
                var header = new MenuItem { Header = provider.Name };
                menu.Items.Add(header);

                header.SubmenuOpened += async (_, __) =>
                {
                    var roles = await provider.GetPossibleRoles(SelectedChampion);
                    header.Items.Clear();

                    foreach (var role in roles)
                    {
                        var roleItem = new MenuItem { Header = role.ToString() };
                        roleItem.Click += async (___, ____) =>
                        {
                            this.Cursor = Cursors.Wait;
                            try
                            {
                                Tree.SetPage(await provider.GetRunePage(SelectedChampion, role));
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(ex);
                                MessageBox.Show(ex.Message);
                            }
                            this.Cursor = Cursors.Arrow;
                        };

                        header.Items.Add(roleItem);
                    }
                };
                header.Items.Add(new MenuItem { Header = Text.Loading, IsEnabled = false });
            }
        }

        private async void ChampionImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var champ = await PickChampionPage.PickChampion(NavigationService, LoL.Champions);

            if (champ.Success)
                await SetChampion(champ.Selected);
        }

        private void Automation_Click(object sender, EventArgs e)
        {
            NavigationService.Navigate(new AutomationPage());
        }

        private void PositionPicker_SelectedChanged(object sender, EventArgs e)
        {
            if (PositionPicker.Selected != Position.UNSELECTED)
            {
                PositionImage.Source = (ImageSource)Application.Current.FindResource(PositionPicker.Selected.ToString());
                UpdateRunePageFromRuneBook();
            }
        }
        
        private void ShowRunes_Click(object sender, EventArgs e)
        {
            Expanded = !Expanded;

            PropertyChanged(this, new PropertyChangedEventArgs(nameof(Expanded)));

            if (Expanded)
            {
                Owner.SetSize(ExpandWidth, ExpandHeight);
                RunesArrow.Source = (ImageSource)Application.Current.FindResource("LeftArrow");
            }
            else
            {
                Owner.SetSize(BaseWidth, BaseHeight);
                RunesArrow.Source = (ImageSource)Application.Current.FindResource("RightArrow");
            }
        }

        private async void Page_Initialized(object sender, EventArgs e)
        {
            Owner.SetSize(this.Width, this.Height);

            await Actuator.Init();
            await SetChampion(null);

            if (Config.Default.LockLoadProvider == null)
            {
                Config.Default.LockLoadProvider =
                    Actuator.RuneProviders.First(o => o.ProviderOptions.HasFlag(Provider.Options.RunePages)
                                                      && !(o is ClientProvider)).Name;
                Config.Default.Save();
            }
            
            if (Config.Default.ItemSetProvider == null)
            {
                Config.Default.ItemSetProvider =
                    Actuator.RuneProviders.First(o => o.ProviderOptions.HasFlag(Provider.Options.ItemSets)).Name;
                Config.Default.Save();
            }

            await new UGGProvider().GetItemSet(6, Position.Fill);
        }

        public void ShowNotification(string title, string message = null, NotificationType type = NotificationType.Information)
            => MainWindow.ShowNotification(title, message, type);

        private void ChampionImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void BugReport_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(Text.ReportBugDialog, "", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                Process.Start("https://github.com/pipe01/legendary-rune-maker/issues/new");
        }
    }
}
