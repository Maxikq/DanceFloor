﻿using Caliburn.Micro;
using Common;
using GameLayer;
using StepMania.Constants;
using StepMania.DebugHelpers;
using StepMania.Views;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace StepMania.ViewModels
{
    public class GameViewModel : BaseViewModel, IHandle<PlayerHitEvent>, IHandle<PlayerMissedEvent>, IHandle<GameActionEvent>, IHandle<KeyPressedEvent>
    {
        GameView _view;
        Storyboard _p1Animation, _p2Animation;
        IGame _game;

        public GameViewModel(IEventAggregator eventAggregator, IGame game)
            : base(eventAggregator)
        {
            _game = game;
            game.GetSongCurrentTime = () => _p1Animation.GetCurrentTime();
        }

        protected override void OnViewAttached(object view, object context)
        {           
            _view = view as GameView;
            _p1Animation = _view.Resources.Values.OfType<Storyboard>().First() as Storyboard;
            _p2Animation = _view.Resources.Values.OfType<Storyboard>().Skip(1).First() as Storyboard;

            PrepareUI();
            StartGame();
        }

        public void LoadSong(ISong song, PlayerID playerId)
        {
            var notes = playerId == PlayerID.Player1 ? _view.p1Notes.Children : _view.p2Notes.Children;
            var animation = playerId == PlayerID.Player1 ? _p1Animation : _p2Animation;

            _game.Song = song;
            notes.Clear();

            //set background
            if (song.BackgroundPath != null)
            {
                _view.Background = new ImageBrush(new BitmapImage(new Uri(song.BackgroundPath)));
            }
            else
            {
                _view.Background = new ImageBrush(new BitmapImage(new Uri(GameUIConstants.DefaultGameBackground)));
            }

            //set animation
            animation.Children.First().Duration = song.Duration;
            (animation.Children.First() as DoubleAnimation).From = -GameUIConstants.ArrowWidthHeight;
            (animation.Children.First() as DoubleAnimation).To = -(GameUIConstants.PixelsPerSecond * song.Duration.TotalSeconds + GameUIConstants.ArrowWidthHeight);

            //create arrows for each sequence element
            foreach(var seqElem in song.Sequences[Difficulty.Easy])
            {
                //load and prepare image
                var bitmap = new BitmapImage(new Uri(playerId == PlayerID.Player1 ? GameUIConstants.P1ArrowImage : GameUIConstants.P2ArrowImage));
                Image img = new Image() { Width = GameUIConstants.ArrowWidthHeight, Height = GameUIConstants.ArrowWidthHeight, Source = bitmap, Tag = seqElem };

                //set top position of arrow according to time
                double top = seqElem.Time.TotalSeconds * GameUIConstants.PixelsPerSecond;
                Canvas.SetTop(img, top);

                //rotate arrow and set in proper place
                switch(seqElem.Type)
                {
                    case SeqElemType.LeftArrow:
                        img.RenderTransform = new RotateTransform(90, GameUIConstants.ArrowWidthHeight / 2.0, GameUIConstants.ArrowWidthHeight / 2.0);
                        Canvas.SetLeft(img, GameUIConstants.LeftArrowX);
                        break;
                    case SeqElemType.DownArrow:
                        Canvas.SetLeft(img, GameUIConstants.DownArrowX);
                        break;
                    case SeqElemType.UpArrow:
                        img.RenderTransform = new RotateTransform(180, GameUIConstants.ArrowWidthHeight / 2.0, GameUIConstants.ArrowWidthHeight / 2.0);
                        Canvas.SetLeft(img, GameUIConstants.UpArrowX);
                        break;
                    case SeqElemType.RightArrow:
                        img.RenderTransform = new RotateTransform(-90, GameUIConstants.ArrowWidthHeight / 2.0, GameUIConstants.ArrowWidthHeight / 2.0);
                        Canvas.SetLeft(img, GameUIConstants.RightArrowX);
                        break;
                }                

                //add image to the canvas
                notes.Add(img);

                DebugSongHelper.AddTimeToNotes(notes, seqElem.Type, top);                
            }            
        }

        public void PrepareUI()
        {
            var song = DebugSongHelper.GenerateSong(270);
            LoadSong(song, PlayerID.Player1);                
            DebugSongHelper.ShowCurrentTimeInsteadPoints(_p1Animation, _game.MusicPlayerService, _view);  

            _view.mainGrid.ColumnDefinitions.Clear();
            _view.mainGrid.ColumnDefinitions.Add(new ColumnDefinition());
            if (_game.Multiplayer)
            {
                LoadSong(song, PlayerID.Player2);
                _view.mainGrid.ColumnDefinitions.Add(new ColumnDefinition());

                _view.p2Playboard.Visibility = System.Windows.Visibility.Visible;
                _view.p2LifePanel.Visibility = System.Windows.Visibility.Visible;
                _view.p2PointsBar.Visibility = System.Windows.Visibility.Visible;                
            }
            else
            {
                _view.p2Playboard.Visibility = System.Windows.Visibility.Hidden;
                _view.p2LifePanel.Visibility = System.Windows.Visibility.Hidden;
                _view.p2PointsBar.Visibility = System.Windows.Visibility.Hidden;   
            }
        }

        bool IsRunning { get; set; }
        public void StartGame()
        {           
            _p1Animation.Begin();
            if (_game.Multiplayer)
                _p2Animation.Begin();
            _game.Start();            
            IsRunning = true;
        }

        public void ResumeGame()
        {
            //sometimes _game.MusicPlayerService.CurrentTime returns 0  o_O  so we have to wait for a normal value
            TimeSpan currentTime;
            while ((currentTime = _game.MusicPlayerService.CurrentTime).TotalSeconds == 0) ; //TODO: possible deadloop, but shouldn't happen

            _p1Animation.Resume();
            _p1Animation.Seek(currentTime); //synchronize animation with music (difference will be about <= 0.05s, which is enough)
            if (_game.Multiplayer)
            {
                _p2Animation.Resume();
                _p2Animation.Seek(currentTime);
            }

            _game.Resume();                        
            IsRunning = true;
        }

        public void PauseGame()
        {
            _p1Animation.Pause();
            if (_game.Multiplayer)
                _p2Animation.Pause();
            _game.Pause();
            IsRunning = false;
        }

        public void StopGame()
        {
            _p1Animation.Stop();
            if (_game.Multiplayer)
                _p2Animation.Stop();
            _game.Stop();
            IsRunning = false;
        }

        public void Handle(PlayerHitEvent message)
        {
            if (!IsActive)
                return;

            var notesPanel = message.PlayerID == PlayerID.Player1 ? _view.p1Notes     : _view.p2Notes;
            var pointsBar  = message.PlayerID == PlayerID.Player1 ? _view.p1PointsBar : _view.p2PointsBar;
            var healthBar  = message.PlayerID == PlayerID.Player1 ? _view.p1Health    : _view.p2Health;

            //set player status
            pointsBar.Points = message.Points.ToString();
            healthBar.SetLife(message.Life);
            DebugSongHelper.ShowHitTimeDifferenceInsteadPoints(_view, _p1Animation);

            //remove hit element
            var toRemove = notesPanel.Children.OfType<Image>().FirstOrDefault(img => img.Tag == message.SequenceElement);
            if (toRemove != null)
                notesPanel.Children.Remove(toRemove);
        }

        public void Handle(PlayerMissedEvent message)
        {
            if (!IsActive)
                return;

            var pointsBar = message.PlayerID == PlayerID.Player1 ? _view.p1PointsBar : _view.p2PointsBar;
            var healthBar = message.PlayerID == PlayerID.Player1 ? _view.p1Health    : _view.p2Health;

            pointsBar.Points = message.Points.ToString();
            healthBar.SetLife(message.Life);

            //todo: play miss sound
        }

        public void Handle(GameActionEvent message)
        {
            if (!IsActive)
                return;

            switch(message.GameAction)
            {
                case GameAction.Pause:
                    PauseGame();
                    break;
                case GameAction.Resume:
                    ResumeGame();
                    break;
                case GameAction.Stop:
                    StopGame();
                    break;
            }
        }

        public void Handle(KeyPressedEvent message)
        {
            if (!IsActive)
                return;

            if (message.Key == Key.Escape)
            {
                StopGame();
                _eventAggregator.Publish(new NavigationEvent() { NavDestination = NavDestination.SongsList });
            }

            DebugSongHelper.HandleKeyPressed(this, message);
        }
    }
}
