﻿using EnvDTE;
using LibGit2Sharp;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GitChangesChecker
{
    internal sealed class GitChangesChecker
    {
        private const int IDOK = 1;
        private const int IDCANCEL = 2;
        private const int IDYES = 6;
        private const int IDNO = 7;

        private readonly DTE _dte;
        private readonly AsyncPackage _package;

        public GitChangesChecker(DTE dte, AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _dte = dte;
        }

        public static GitChangesChecker Instance
        {
            get;
            private set;
        }

        public static async System.Threading.Tasks.Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in FirstCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;

            Instance = new GitChangesChecker(dte, package);
            dte.Events.SolutionEvents.QueryCloseSolution += Instance.OnQueryCloseSolutionEventHandler;
        }

        private void OnQueryCloseSolutionEventHandler([In][Out] ref bool fCancel)
        {
            // https://learn.microsoft.com/en-us/visualstudio/extensibility/managing-project-loading-in-a-solution?view=vs-2022
            ThreadHelper.ThrowIfNotOnUIThread();

            var solutionDirectory = System.IO.Path.GetDirectoryName(_dte.Solution.FullName);
           

            var repoPath = Path.Combine(solutionDirectory, ".git");
            if (!Directory.Exists(repoPath))
                return;

            var checkResult = CheckGitRepo(repoPath);
            ShowMessageBox(checkResult, ref fCancel);
        }

        private void ShowMessageBox(RepoCheckResult result, [In][Out] ref bool fCancel)
        {
            switch (result)
            {
                case RepoCheckResult.NoChanges:
                    break;
                case RepoCheckResult.IsDirty:
                    ShowMessageBox(MessageIsDirty, TitleIsDirty, ref fCancel);
                    break;
                case RepoCheckResult.UnpushedChanges:
                    ShowMessageBox(MessageUnpushedChanges, TitleUnpushedChanges, ref fCancel);
                    break;
            }
        }

        private void ShowMessageBox(string message, string title, [In][Out] ref bool fCancel)
        {
            var returnValue = VsShellUtilities.ShowMessageBox(
                _package,
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            switch (returnValue)
            {
                case IDOK:
                    fCancel = true;
                    break;
                case IDCANCEL:
                    fCancel = false;
                    break;
                case IDYES:
                    fCancel = true;
                    break;
                case IDNO:
                    fCancel = false;
                    break;
                default:
                    break;
            }
        }

        private static RepoCheckResult CheckGitRepo(string repoPath)
        {
            using (var repo = new Repository(repoPath))
            {
                var status = repo.RetrieveStatus();
                if(status.IsDirty)
                {
                    return RepoCheckResult.IsDirty;
                }

                if(HasGitBranchUnpushedChanges(repo.Head))
                {
                    return RepoCheckResult.UnpushedChanges;
                }

                return RepoCheckResult.NoChanges;
            }
        }

        private static bool HasGitBranchUnpushedChanges(Branch branch)
        {
            return branch.TrackingDetails.AheadBy.Value > 0;
        }

        enum RepoCheckResult
        {
            NoChanges = 0,
            IsDirty,
            UnpushedChanges
        }

        private static string MessageIsDirty = string.Format(CultureInfo.CurrentCulture, "Uncommitted git changes, do you want to commit before closing?");
        private const string TitleIsDirty = "Uncommitted git changes!";

        private static string MessageUnpushedChanges = string.Format(CultureInfo.CurrentCulture, "Unpushed git commits, do you want to push before closing?");
        private const string TitleUnpushedChanges = "Unpuished git commits!";
    }
}
