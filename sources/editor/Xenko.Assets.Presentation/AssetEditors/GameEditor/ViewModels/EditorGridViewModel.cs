// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using Xenko.Core.Annotations;
using Xenko.Core.Mathematics;
using Xenko.Core.Presentation.Commands;
using Xenko.Core.Presentation.ViewModel;
using Xenko.Assets.Presentation.AssetEditors.GameEditor.Services;
using Xenko.Assets.Presentation.SceneEditor;

namespace Xenko.Assets.Presentation.AssetEditors.GameEditor.ViewModels
{
    /// <summary>
    /// A view model controlling a grid in an editor game.
    /// </summary>
    public class EditorGridViewModel : DispatcherViewModel
    {
        private readonly IEditorGameController controller;

        /// <summary>
        /// Initializes a new instance of the <see cref="EditorGridViewModel"/> class.
        /// </summary>
        /// <param name="serviceProvider">The service provider for this view model.</param>
        /// <param name="controller">The controller object for the related editor game.</param>
        public EditorGridViewModel([NotNull] IViewModelServiceProvider serviceProvider, [NotNull] IEditorGameController controller)
            : base(serviceProvider)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            this.controller = controller;
            ToggleCommand = new AnonymousCommand(ServiceProvider, () => IsVisible = !IsVisible);
        }

        /// <summary>
        /// Gets or sets whether the grid is currently visible.
        /// </summary>
        public bool IsVisible { get { return Service.IsActive; } set { SetValue(IsVisible != value, () => Service.IsActive = value); } }

        /// <summary>
        /// Gets or sets the color to apply to the grid.
        /// </summary>
        public Color3 Color { get { return Service.Color; } set { SetValue(Color != value, () => Service.Color = value); } }

        /// <summary>
        /// Gets or sets the alpha level of the grid.
        /// </summary>
        public float Alpha { get { return Service.Alpha; } set { SetValue(Math.Abs(Alpha - value) > MathUtil.ZeroTolerance, () => Service.Alpha = value); } }

        /// <summary>
        /// Gets or sets the axis of the grid.
        /// </summary>
        public int AxisIndex { get { return Service.AxisIndex; } set { SetValue(AxisIndex != value, () => Service.AxisIndex = value); } }

        /// <summary>
        /// Gets a command that will toggle the visibility of the grid.
        /// </summary>
        public ICommandBase ToggleCommand { get; }

        private IEditorGameGridViewModelService Service => controller.GetService<IEditorGameGridViewModelService>();

        public void LoadSettings([NotNull] SceneSettingsData sceneSettings)
        {
            IsVisible = sceneSettings.GridVisible;
            Color = sceneSettings.GridColor;
            Alpha = sceneSettings.GridOpacity;
            AxisIndex = sceneSettings.GridAxisIndex;
        }

        public void SaveSettings([NotNull] SceneSettingsData sceneSettings)
        {
            sceneSettings.GridVisible = IsVisible;
            sceneSettings.GridColor = Color;
            sceneSettings.GridOpacity = Alpha;
            sceneSettings.GridAxisIndex = AxisIndex;
        }
    }
}
