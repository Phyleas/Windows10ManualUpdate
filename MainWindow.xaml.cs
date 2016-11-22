﻿//
// Windows 10 Manual Update
// Copyright 2016 Vyacheslav Napadovsky.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace w10mu {

    class UpdateItem {
        dynamic _update;

        public UpdateItem(dynamic update) {
            _update = update;

            //IsChecked = _update.AutoSelectOnWebSites;     // this line will select them all
            IsChecked = _update.IsMandatory;

            var sb = new StringBuilder();
            if (RequireUserInput)
                sb.Append("[REQUIRE USER INPUT] ");
            if (EulaAccepted == false)
                sb.Append("[EULA NOT ACCEPTED] ");
            sb.AppendFormat("{0}\n", Title);
            if (_update.Description != null)
                sb.AppendFormat("{0}\n", _update.Description);
            if (_update.MoreInfoUrls != null && _update.MoreInfoUrls.Count > 0) {
                sb.AppendFormat("More info:\n");
                for (int i = 0; i < _update.MoreInfoUrls.Count; ++i)
                    sb.AppendFormat("{0}\n", _update.MoreInfoUrls.Item(i));
            }
            if (_update.EulaText != null)
                sb.AppendFormat("EULA TEXT:\n{0}\n", _update.EulaText);

            dynamic bundle = _update.BundledUpdates;
            if (bundle != null && bundle.Count > 0) {
                sb.AppendFormat("This update contains {0} packages:\n", bundle.Count);
                for (int i = 0; i < bundle.Count; ++i) {
                    var item = new UpdateItem(bundle.Item(i));
                    var desc = item.Description;
                    desc = desc.Substring(0, desc.Length - 1);
                    sb.AppendFormat("#{0}: {1}\n", i + 1, desc.Replace("\n", "\n * "));
                }
            }

            Description = sb.ToString();
        }

        public bool IsChecked { get; set; }
        public string Title { get { return _update.Title; } }
        public string Description { get; }
        //public bool IsHidden { get; set; }

        public dynamic Update { get { return _update; } }
        public bool RequireUserInput { get { return _update.InstallationBehavior.CanRequestUserInput; } }
        public bool EulaAccepted { get { return _update.EulaAccepted; } }

        public Brush Background {
            get {
                return Brushes.Transparent;
                //return IsHidden ? SystemColors.InactiveCaptionTextBrush : Brushes.Transparent;
            }
        }

    }

    public partial class MainWindow : Window {

        private dynamic _updateSession = null;
        private dynamic _updateSearcher = null;
        private dynamic _searchResult = null;

        async Task SearchForUpdates() {
            _status.Text = "Searching for updates...";
            await Task.Run(() => {
                _searchResult = _updateSearcher.Search("IsInstalled=0 and Type='Software' and IsHidden=0");
            });
            _status.Text = "Search completed.";
            var list = new List<UpdateItem>();
            int count = _searchResult.Updates.Count;
            _installButton.IsEnabled = count > 0;
            if (count > 0) {
                for (int i = 0; i < _searchResult.Updates.Count; ++i)
                    list.Add(new UpdateItem(_searchResult.Updates.Item(i)));
            }
            else {
                _status.Text = "There are no applicable updates.";
            }
            _list.ItemsSource = list;
        }

        protected override async void OnActivated(EventArgs e) {
            base.OnActivated(e);
            if (_updateSession == null) {
                try {
                    _updateSession = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session"));
                    _updateSession.ClientApplicationID = "Windows 10 Manual Update";
                    _updateSearcher = _updateSession.CreateUpdateSearcher();
                    await SearchForUpdates();
                    _installButton.IsEnabled = true;
                }
                catch (Exception ex) {
                    MessageBox.Show(this, ex.ToString(), "Exception has occured!");
                }
            }
        }

        public MainWindow() {
            InitializeComponent();
        }

        private async void Install_Click(object sender, RoutedEventArgs e) {
            _installButton.IsEnabled = false;

            try {
                var list = _list.ItemsSource as List<UpdateItem>;
                dynamic updatesToInstall = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl"));
                foreach (var item in list) {
                    if (!item.IsChecked)
                        continue;
                    if (item.RequireUserInput)
                        continue;
                    if (!item.EulaAccepted) {
                        if (MessageBox.Show(this, item.Update.EulaText, "Do you accept this license agreement?", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                            continue;
                        item.Update.AcceptEula();
                    }
                    updatesToInstall.Add(item.Update);
                }
                if (updatesToInstall.Count == 0) {
                    _status.Text = "All applicable updates were skipped.";
                }
                else {
                    _status.Text = "Downloading updates...";
                    dynamic downloader = _updateSession.CreateUpdateDownloader();
                    downloader.Updates = updatesToInstall;
                    await Task.Run(() => { downloader.Download(); });

                    bool rebootMayBeRequired = false;
                    for (int i = 0; i < updatesToInstall.Count; ++i)
                        if (updatesToInstall.Item(i).InstallationBehavior.RebootBehavior > 0)
                            rebootMayBeRequired = true;

                    string warnText = rebootMayBeRequired
                        ? "These updates may require a reboot."
                        : "Installation ready.";

                    if (MessageBox.Show(this, warnText + " Continue?", "Notice", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                        _status.Text = "Installing updates...";

                        dynamic installer = _updateSession.CreateUpdateInstaller();
                        installer.Updates = updatesToInstall;
                        dynamic installationResult = null;
                        await Task.Run(() => { installationResult = installer.Install(); });

                        var sb = new StringBuilder();
                        if (installationResult.RebootRequired == true)
                            sb.Append("[REBOOT REQUIRED] ");
                        sb.AppendFormat("Code: {0}", installationResult.ResultCode);
                        sb.Append("Listing of updates installed:\n");
                        for (int i = 0; i < updatesToInstall.Count; ++i) {
                            sb.AppendFormat("{0} : {1}\n",
                                installationResult.GetUpdateResult(i).ResultCode,
                                updatesToInstall.Item(i).Title);
                        }
                        MessageBox.Show(this, sb.ToString(), "Installation Result");
                    }
                    await SearchForUpdates();
                }
            }
            catch (Exception ex) {
                MessageBox.Show(this, ex.ToString(), "Exception has occured!");
            }

            _installButton.IsEnabled = true;
        }

        private void ListSelectionChanged(object sender, SelectionChangedEventArgs e) {
            foreach (UpdateItem item in e.AddedItems) {
                _description.Document.Blocks.Clear();
                var p = new Paragraph();
                p.Inlines.Add(new Run(item.Description));
                p.EnableHyperlinks();
                _description.Document.Blocks.Add(p);
                break;
            }
        }

    }

    static class RichEditExtensions {
        public static void EnableHyperlinks(this Paragraph p) {
            string paragraphText = new TextRange(p.ContentStart, p.ContentEnd).Text;
            foreach (string word in paragraphText.Split(' ', '\n', '\t').ToList()) {
                if (word.IndexOf("//") != -1 && Uri.IsWellFormedUriString(word, UriKind.Absolute)) {
                    Uri uri = new Uri(word, UriKind.RelativeOrAbsolute);
                    if (!uri.IsAbsoluteUri)
                        uri = new Uri(@"http://" + word, UriKind.Absolute);
                    for (TextPointer position = p.ContentStart;
                        position != null;
                        position = position.GetNextContextPosition(LogicalDirection.Forward))
                    {
                        if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text) {
                            string textRun = position.GetTextInRun(LogicalDirection.Forward);
                            int indexInRun = textRun.IndexOf(word);
                            if (indexInRun >= 0) {
                                TextPointer start = position.GetPositionAtOffset(indexInRun);
                                TextPointer end = start.GetPositionAtOffset(word.Length);
                                var link = new Hyperlink(start, end);
                                link.NavigateUri = uri;
                                link.RequestNavigate += (sender, args) => Process.Start(args.Uri.ToString());
                                break;
                            }
                        }
                    }
                }
            }
        }

    }

}
