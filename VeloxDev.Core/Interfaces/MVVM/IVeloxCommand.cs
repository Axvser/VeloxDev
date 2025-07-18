﻿using System.Windows.Input;

namespace VeloxDev.Core.Interfaces.MVVM
{
    public interface IVeloxCommand : ICommand
    {
        void OnCanExecuteChanged();
        Task ExecuteAsync(object? parameter);
    }
}
