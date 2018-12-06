﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using AsyncAwaitBestPractices.MVVM;
using TextMood.Shared;

using Xamarin.Forms;

namespace TextMood
{
    public class TextResultsListViewModel : BaseViewModel
    {
        #region Fields
        bool _isRefreshing;
        Color _backgroundColor;
        ObservableCollection<ITextMoodModel> _textList;
        ICommand _pullToRefreshCommand, _addTextMoodModelCommand;
        #endregion

        #region Events
        public event EventHandler<string> ErrorTriggered;
        public event EventHandler PhilipsHueBridgeConnectionFailed;
        #endregion

        #region Properties
        public ICommand AddTextMoodModelCommand => _addTextMoodModelCommand ??
            (_addTextMoodModelCommand = new AsyncCommand<ITextMoodModel>(ExecuteAddTextMoodModelCommand, continueOnCapturedContext: false));


        public ICommand PullToRefreshCommand => _pullToRefreshCommand ??
            (_pullToRefreshCommand = new AsyncCommand(ExecutePullToRefreshCommand, continueOnCapturedContext: false));

        public ObservableCollection<ITextMoodModel> TextList
        {
            get => _textList;
            set => SetProperty(ref _textList, value);
        }

        public Color BackgroundColor
        {
            get => _backgroundColor;
            set => SetProperty(ref _backgroundColor, value);
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }
        #endregion

        #region Methods
        async Task ExecutePullToRefreshCommand()
        {
            await UpdateTextResultsListFromRemoteDatabase().ConfigureAwait(false);

            var averageSentiment = TextMoodModelServices.GetAverageSentimentScore(TextList);

            SetTextResultsListBackgroundColor(averageSentiment);

            await UpdatePhilipsHueLight(averageSentiment).ConfigureAwait(false);
        }

        Task ExecuteAddTextMoodModelCommand(ITextMoodModel textMoodModel)
        {
            if (TextList.Any(x => x.Id.Equals(textMoodModel.Id)))
                return Task.CompletedTask;

            TextList.Insert(0, textMoodModel);

            var averageSentiment = TextMoodModelServices.GetAverageSentimentScore(TextList);

            SetTextResultsListBackgroundColor(averageSentiment);

            return UpdatePhilipsHueLight(averageSentiment);
        }

        async Task UpdateTextResultsListFromRemoteDatabase()
        {
            try
            {
                IsRefreshing = true;

                var textMoodList = await TextResultsService.GetTextModels().ConfigureAwait(false);
                var recentTextMoodList = TextMoodModelServices.GetRecentTextModels(new List<ITextMoodModel>(textMoodList), TimeSpan.FromHours(1));

                TextList = new ObservableCollection<ITextMoodModel>(recentTextMoodList.OrderByDescending(x => x.CreatedAt));
            }
            catch (Exception e) when (e?.InnerException?.Message != null)
            {
                DebugServices.Report(e);
                OnErrorTriggered(e.InnerException.Message);
            }
            catch (Exception e)
            {
                DebugServices.Report(e);
                OnErrorTriggered(e.Message);
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        void SetTextResultsListBackgroundColor(double averageSentiment)
        {
            var (red, green, blue) = TextMoodModelServices.GetRGBFromSentimentScore(averageSentiment);
            BackgroundColor = Color.FromRgba(red, green, blue, 0.5);
        }

        async Task UpdatePhilipsHueLight(double averageSentiment)
        {
            if (!PhilipsHueBridgeSettings.IsEnabled)
                return;

            try
            {
                var (red, green, blue) = TextMoodModelServices.GetRGBFromSentimentScore(averageSentiment);
                var hue = PhilipsHueServices.ConvertToHue(red, green, blue);

                await PhilipsHueBridgeAPIServices.UpdateLightBulbColor(PhilipsHueBridgeSettings.IPAddress.ToString(),
                                                                        PhilipsHueBridgeSettings.Username,
                                                                        hue).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                DebugServices.Report(e);
                OnPhilipsHueBridgeConnectionFailed();
            }
        }

        void OnErrorTriggered(string message) => ErrorTriggered?.Invoke(this, message);
        void OnPhilipsHueBridgeConnectionFailed() => PhilipsHueBridgeConnectionFailed?.Invoke(this, EventArgs.Empty);
        #endregion
    }
}
