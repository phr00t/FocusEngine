// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using Xenko.Core.Mathematics;
using Xenko.Engine;
using Xenko.Games;
using Xenko.Graphics;
using Xenko.Input;
using Xenko.UI;

namespace Xenko.Rendering.UI
{
    public partial class UIRenderFeature 
    {
        // object to avoid allocation at each element leave event
        [ThreadStatic]
        private static HashSet<UIElement> newlySelectedElementParents = new HashSet<UIElement>();

        internal static UIElement ForceMouseOver;

        partial void PickingUpdate(RenderUIElement renderUIElement, Viewport viewport, ref Matrix worldViewProj, GameTime drawTime, List<PointerEvent> events)
        {
            if (renderUIElement.Page?.RootElement == null)
                return;

            var inverseZViewProj = worldViewProj;
            inverseZViewProj.Row3 = -inverseZViewProj.Row3;

            if (UpdateMouseOver(ref viewport, ref inverseZViewProj, renderUIElement, drawTime))
            {
                UpdateTouchEvents(ref viewport, ref inverseZViewProj, renderUIElement, drawTime, events);
            }
        }

        partial void PickingPrepare(List<PointerEvent> compactedPointerEvents)
        {
            // compact all the pointer events that happened since last frame to avoid performing useless hit tests.
            if (input == null) // no input for thumbnails
                return;

            // compact all the move events of the frame together
            var aggregatedTranslation = Vector2.Zero;
            for (var index = 0; index < input.PointerEvents.Count; ++index)
            {
                var pointerEvent = input.PointerEvents[index];

                if (pointerEvent.EventType != PointerEventType.Moved)
                {
                    aggregatedTranslation = Vector2.Zero;
                    compactedPointerEvents.Add(pointerEvent.Clone());
                    continue;
                }

                aggregatedTranslation += pointerEvent.DeltaPosition;

                if (index + 1 >= input.PointerEvents.Count || input.PointerEvents[index + 1].EventType != PointerEventType.Moved)
                {
                    var compactedMoveEvent = pointerEvent.Clone();
                    compactedMoveEvent.DeltaPosition = aggregatedTranslation;
                    compactedPointerEvents.Add(compactedMoveEvent);
                }
            }

            return;
        }

        /// <summary>
        /// Creates a ray in object space based on a screen position and a previously rendered object's WorldViewProjection matrix
        /// </summary>
        /// <param name="viewport">The viewport in which the object was rendered</param>
        /// <param name="screenPos">The click position on screen in normalized (0..1, 0..1) range</param>
        /// <param name="worldViewProj">The WorldViewProjection matrix with which the object was last rendered in the view</param>
        /// <returns></returns>
        private Ray GetWorldRay(ref Vector3 resolution, ref Viewport viewport, Vector2 screenPos, ref Matrix worldViewProj)
        {
            var graphicsDevice = graphicsDeviceService?.GraphicsDevice;
            if (graphicsDevice == null)
                return new Ray(new Vector3(float.NegativeInfinity), new Vector3(0, 1, 0));

            screenPos.X *= graphicsDevice.Presenter.BackBuffer.Width;
            screenPos.Y *= graphicsDevice.Presenter.BackBuffer.Height;

            var unprojectedNear = viewport.Unproject(new Vector3(screenPos, 0.0f), ref worldViewProj);

            var unprojectedFar = viewport.Unproject(new Vector3(screenPos, 1.0f), ref worldViewProj);

            var rayDirection = Vector3.Normalize(unprojectedFar - unprojectedNear);
            var clickRay = new Ray(unprojectedNear, rayDirection);

            return clickRay;
        }

        /// <summary>
        /// Returns if a screen position is within the borders of a tested ui component
        /// </summary>
        /// <param name="uiComponent">The <see cref="UIComponent"/> to be tested</param>
        /// <param name="viewport">The <see cref="Viewport"/> in which the component is being rendered</param>
        /// <param name="worldViewProj"></param>
        /// <param name="screenPosition">The position of the lick on the screen in normalized (0..1, 0..1) range</param>
        /// <param name="uiRay"><see cref="Ray"/> from the click in object space of the ui component in (-Resolution.X/2 .. Resolution.X/2, -Resolution.Y/2 .. Resolution.Y/2) range</param>
        /// <returns></returns>
        private bool GetTouchPosition(Vector3 resolution, ref Viewport viewport, ref Matrix worldViewProj, Vector2 screenPosition, out Ray uiRay)
        {
            // Get a touch ray in object (UI component) space
            uiRay = GetWorldRay(ref resolution, ref viewport, screenPosition, ref worldViewProj);

            // If the click point is outside the canvas ignore any further testing
            var dist = -uiRay.Position.Z / uiRay.Direction.Z;
            if (Math.Abs(uiRay.Position.X + uiRay.Direction.X * dist) > resolution.X * 0.5f ||
                Math.Abs(uiRay.Position.Y + uiRay.Direction.Y * dist) > resolution.Y * 0.5f)
            {
                ForceMouseOver = null;
                return false;
            }

            return true;
        }

        private void MakeTouchEvent(UIElement currentTouchedElement, UIElement lastTouchedElement, PointerEventType type, int ButtonId,
                                    Vector2 screenPos, Vector2 screenTranslation, Vector3 worldPos, Vector3 worldTranslation, GameTime time)
        {
            var touchEvent = new TouchEventArgs
            {
                Action = TouchAction.Down,
                Timestamp = time.Total,
                ScreenPosition = screenPos,
                ScreenTranslation = screenTranslation,
                WorldPosition = worldPos,
                WorldTranslation = worldTranslation,
                ButtonId = ButtonId
            };

            switch (type)
            {
                case PointerEventType.Pressed:
                    touchEvent.Action = TouchAction.Down;
                    currentTouchedElement?.RaiseTouchDownEvent(touchEvent);
                    break;

                case PointerEventType.Released:
                    touchEvent.Action = TouchAction.Up;

                    // generate enter/leave events if we passed from an element to another without move events
                    if (currentTouchedElement != lastTouchedElement)
                        ThrowEnterAndLeaveTouchEvents(currentTouchedElement, lastTouchedElement, touchEvent);

                    // trigger the up event
                    currentTouchedElement?.RaiseTouchUpEvent(touchEvent);
                    break;

                case PointerEventType.Moved:
                    touchEvent.Action = TouchAction.Move;

                    // first notify the move event (even if the touched element changed in between it is still coherent in one of its parents)
                    currentTouchedElement?.RaiseTouchMoveEvent(touchEvent);

                    // then generate enter/leave events if we passed from an element to another
                    if (currentTouchedElement != lastTouchedElement)
                        ThrowEnterAndLeaveTouchEvents(currentTouchedElement, lastTouchedElement, touchEvent);
                    break;

                case PointerEventType.Canceled:
                    touchEvent.Action = TouchAction.Move;

                    // generate enter/leave events if we passed from an element to another without move events
                    if (currentTouchedElement != lastTouchedElement)
                        ThrowEnterAndLeaveTouchEvents(currentTouchedElement, lastTouchedElement, touchEvent);

                    // then raise leave event to all the hierarchy of the previously selected element.
                    var element = currentTouchedElement;
                    while (element != null)
                    {
                        if (element.IsTouched)
                            element.RaiseTouchLeaveEvent(touchEvent);
                        element = element.VisualParent;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void UpdateTouchEvents(ref Viewport viewport, ref Matrix worldViewProj, RenderUIElement state, GameTime gameTime, List<PointerEvent> compactedPointerEvents)
        {
            var rootElement = state.Page.RootElement;
            var intersectionPoint = Vector3.Zero;
            var lastTouchPosition = new Vector2(float.NegativeInfinity);

            // analyze pointer event input and trigger UI touch events depending on hit Tests
            for (int i=0; i<compactedPointerEvents.Count; i++)
            {
                var pointerEvent = compactedPointerEvents[i];

                // performance optimization: skip all the events that started outside of the UI
                var lastTouchedElement = state.LastTouchedElement;
                if (lastTouchedElement == null && pointerEvent.EventType != PointerEventType.Pressed)
                    continue;

                var currentTouchPosition = pointerEvent.Position;
                var currentTouchedElement = lastTouchedElement;

                // re-calculate the element under cursor if click position changed.
                if (lastTouchPosition != currentTouchPosition)
                {
                    Ray uiRay;
                    if (!GetTouchPosition(state.Resolution, ref viewport, ref worldViewProj, currentTouchPosition, out uiRay))
                        continue;

                    currentTouchedElement = GetElementAtScreenPosition(rootElement, ref uiRay, ref worldViewProj, ref intersectionPoint);
                }

                if (pointerEvent.EventType == PointerEventType.Pressed || pointerEvent.EventType == PointerEventType.Released)
                    state.LastIntersectionPoint = intersectionPoint;

                MakeTouchEvent(currentTouchedElement, lastTouchedElement, pointerEvent.EventType, pointerEvent.PointerId,
                               currentTouchPosition, pointerEvent.DeltaPosition, intersectionPoint,
                               intersectionPoint - state.LastIntersectionPoint, gameTime);

                lastTouchPosition = currentTouchPosition;
                state.LastTouchedElement = currentTouchedElement;
                state.LastIntersectionPoint = intersectionPoint;
            }
        }

        /// <summary>
        /// If a pointer is pointed at an UIElement, it will be set here
        /// </summary>
        [ThreadStatic]
        private static UIElement UIElementUnderMouseCursor;

        private void copyLastUiComponentAverage(RenderUIElement root)
        {
            if ((root.Component.AveragedPositions?.Length ?? 0) > 1)
            {
                // copy last entry
                Vector2 lastEntry = root.Component.AveragedPositions[root.Component.AveragePositionIndex];
                root.Component.AveragePositionIndex = (root.Component.AveragePositionIndex + 1) % root.Component.AveragedPositions.Length;
                root.Component.AveragedPositions[root.Component.AveragePositionIndex] = lastEntry;
            }
        }

        private void TrackUIPointer(ref Ray r, RenderUIElement root, bool nonui)
        {
            if (root.Component.AlwaysTrackPointer == false)
                return;

            UIElement rootElement = root.Page.RootElement;

            if (rootElement.Intersects(ref r, out var intersectionPoint, nonui, root.Component.TrackedCanvasScale))
            {
                Vector2 pos;

                if (nonui)
                {
                    root.WorldMatrix3D.Decompose(out Vector3 scale, out Matrix rotation, out Vector3 translation);
                    Vector3 pos3d = intersectionPoint - translation;
                    rotation.Invert();
                    pos = Vector3.Transform(pos3d, rotation).XY() / scale.XY();
                    pos.Y = root.Resolution.Y * 0.5f - pos.Y;
                } 
                else
                {
                    pos = intersectionPoint.XY();
                    pos.Y += root.Resolution.Y * 0.5f;
                }
                pos.X += root.Resolution.X * 0.5f;
                pos.X -= rootElement.RenderOffsets.X;
                pos.Y -= rootElement.RenderOffsets.Y;

                root.Component.AveragePositionIndex = (root.Component.AveragePositionIndex + 1) % root.Component.AveragedPositions.Length;
                root.Component.AveragedPositions[root.Component.AveragePositionIndex] = pos;
            }
            else copyLastUiComponentAverage(root);
        }

        private bool UpdateMouseOver(ref Viewport viewport, ref Matrix worldViewProj, RenderUIElement state, GameTime time)
        {
            bool VRcontrollerUsed = VirtualReality.VRDeviceSystem.VRActive && (TransformComponent.LastLeftHandTracked != null || TransformComponent.LastRightHandTracked != null);

            if (input == null || !input.HasMouse && VRcontrollerUsed == false || VRcontrollerUsed && state.Source is UIComponent uicomp && uicomp.IsFullScreen)
                return false;

            var intersectionPoint = Vector3.Zero;
            var rootElement = state.Page.RootElement;
            var lastMouseOverElement = state.LastMouseOverElement;
            UIElementUnderMouseCursor = lastMouseOverElement;

            if (VRcontrollerUsed)
            {
                for (int i=0; i<2; i++)
                {
                    int swappedIndex = VirtualReality.VRDeviceSystem.GetSystem.SwapControllerHands ? (i ^ 1) : i;
                    TransformComponent useHand = swappedIndex == 0 ?
                                                    (TransformComponent.OverrideRightHandUIPointer ?? TransformComponent.LastRightHandTracked) :
                                                    (TransformComponent.OverrideLeftHandUIPointer ?? TransformComponent.LastLeftHandTracked);

                    if (useHand != null)
                    {
                        Ray uiRay = new Ray(useHand.WorldPosition(), useHand.Forward(true));
                        
                        if ((swappedIndex ^ 1) == (int)VirtualReality.VRDeviceSystem.UITrackingHand)
                            TrackUIPointer(ref uiRay, state, true);
                        
                        UIElementUnderMouseCursor = GetElementAtWorldPosition(rootElement, ref uiRay, ref worldViewProj, ref intersectionPoint);
                        if (UIElementUnderMouseCursor != null)
                        {
                            // is this a forced element from another uicomponent?
                            if (ForceMouseOver == UIElementUnderMouseCursor && state.Source is UIComponent uic &&
                                uic.Page?.RootElement?.VisualChildren is UIElementCollection coll &&
                                coll.Contains(ForceMouseOver) == false) continue;

                            // wait, are we selecting this element?
                            VirtualReality.TouchController tc = VirtualReality.VRDeviceSystem.GetSystem.GetController(swappedIndex == 0 ? VirtualReality.TouchControllerHand.Right : VirtualReality.TouchControllerHand.Left);

                            if (tc != null)
                            {
                                // adjust intersection point into local UI space from world space
                                intersectionPoint = (intersectionPoint - UIElementUnderMouseCursor.WorldMatrix3D.TranslationVector) / state.WorldMatrix3D.ScaleVector;

                                // check the first button
                                if (tc.IsTouchedDown(VirtualReality.VRDeviceSystem.UIActivationButton) || tc.IsPressedDown(VirtualReality.VRDeviceSystem.UIActivationButton))
                                    MakeTouchEvent(UIElementUnderMouseCursor, lastMouseOverElement, PointerEventType.Pressed, 0, Vector2.Zero, Vector2.Zero, intersectionPoint, Vector3.Zero, time);
                                else if (tc.IsTouchReleased(VirtualReality.VRDeviceSystem.UIActivationButton) || tc.IsPressReleased(VirtualReality.VRDeviceSystem.UIActivationButton))
                                    MakeTouchEvent(UIElementUnderMouseCursor, lastMouseOverElement, PointerEventType.Released, 0, Vector2.Zero, Vector2.Zero, intersectionPoint, Vector3.Zero, time);
                                else if (tc.IsTouched(VirtualReality.VRDeviceSystem.UIActivationButton) || tc.IsPressed(VirtualReality.VRDeviceSystem.UIActivationButton))
                                    MakeTouchEvent(UIElementUnderMouseCursor, lastMouseOverElement, PointerEventType.Moved, 0, Vector2.Zero, Vector2.Zero, intersectionPoint, state.LastIntersectionPoint - intersectionPoint, time);

                                // check the second button
                                else if (VirtualReality.VRDeviceSystem.UIActivationButton2.HasValue)
                                {
                                    var button = VirtualReality.VRDeviceSystem.UIActivationButton2.Value;

                                    if (tc.IsTouchedDown(button) || tc.IsPressedDown(button))
                                        MakeTouchEvent(UIElementUnderMouseCursor, lastMouseOverElement, PointerEventType.Pressed, 2, Vector2.Zero, Vector2.Zero, intersectionPoint, Vector3.Zero, time);
                                    else if (tc.IsTouchReleased(button) || tc.IsPressReleased(button))
                                        MakeTouchEvent(UIElementUnderMouseCursor, lastMouseOverElement, PointerEventType.Released, 2, Vector2.Zero, Vector2.Zero, intersectionPoint, Vector3.Zero, time);
                                    else if (tc.IsTouched(button) || tc.IsPressed(button))
                                        MakeTouchEvent(UIElementUnderMouseCursor, lastMouseOverElement, PointerEventType.Moved, 2, Vector2.Zero, Vector2.Zero, intersectionPoint, state.LastIntersectionPoint - intersectionPoint, time);
                                }

                                state.LastIntersectionPoint = intersectionPoint;
                            }
                            break;
                        }
                    }
                }
            }
            else 
            {
                var mousePosition = input.MousePosition;

                // determine currently overred element.
                if (mousePosition != state.LastMousePosition)
                {
                    state.LastMousePosition = mousePosition;
                    // check if we touch anything of importance
                    bool hitElement = GetTouchPosition(state.Resolution, ref viewport, ref worldViewProj, mousePosition, out Ray uiRay);
                    // update tracked position on canvas, if this is enabled
                    TrackUIPointer(ref uiRay, state, false);
                    // bail out early if we didn't touch anything
                    if (!hitElement) return true;
                    UIElementUnderMouseCursor = GetElementAtScreenPosition(rootElement, ref uiRay, ref worldViewProj, ref intersectionPoint);
                }
                else copyLastUiComponentAverage(state);
            }

            // find the common parent between current and last overred elements
            var commonElement = FindCommonParent(UIElementUnderMouseCursor, lastMouseOverElement);

            // disable mouse over state to previously overred hierarchy
            var parent = lastMouseOverElement;
            while (parent != commonElement && parent != null)
            {
                parent.MouseOverState = MouseOverState.MouseOverNone;
                parent = parent.VisualParent;
            }

            // enable mouse over state to currently overred hierarchy
            if (UIElementUnderMouseCursor != null)
            {
                // the element itself
                UIElementUnderMouseCursor.MouseOverState = MouseOverState.MouseOverElement;
                // its hierarchy
                parent = UIElementUnderMouseCursor.VisualParent;
                while (parent != null)
                {
                    if (parent.IsHierarchyEnabled)
                        parent.MouseOverState = MouseOverState.MouseOverChild;

                    parent = parent.VisualParent;
                }
            }
            // update cached values
            state.LastMouseOverElement = UIElementUnderMouseCursor;
            return !VRcontrollerUsed;
        }

        private UIElement FindCommonParent(UIElement element1, UIElement element2)
        {
            // build the list of the parents of the newly selected element
            if (newlySelectedElementParents == null)
                newlySelectedElementParents = new HashSet<UIElement>();
            else 
                newlySelectedElementParents.Clear();
            
            var newElementParent = element1;
            while (newElementParent != null)
            {
                newlySelectedElementParents.Add(newElementParent);
                newElementParent = newElementParent.VisualParent;
            }

            // find the common element into the previously and newly selected element hierarchy
            var commonElement = element2;
            while (commonElement != null && !newlySelectedElementParents.Contains(commonElement))
                commonElement = commonElement.VisualParent;

            return commonElement;
        }

        private void ThrowEnterAndLeaveTouchEvents(UIElement currentElement, UIElement previousElement, TouchEventArgs touchEvent)
        {
            var commonElement = FindCommonParent(currentElement, previousElement);

            // raise leave events to the hierarchy: previousElt -> commonElementParent
            var previousElementParent = previousElement;
            while (previousElementParent != commonElement && previousElementParent != null)
            {
                if (previousElementParent.IsHierarchyEnabled && previousElementParent.IsTouched)
                {
                    touchEvent.Handled = false; // reset 'handled' because it corresponds to another event
                    previousElementParent.RaiseTouchLeaveEvent(touchEvent);
                }
                previousElementParent = previousElementParent.VisualParent;
            }

            // raise enter events to the hierarchy: newElt -> commonElementParent
            var newElementParent = currentElement;
            while (newElementParent != commonElement && newElementParent != null)
            {
                if (newElementParent.IsHierarchyEnabled && !newElementParent.IsTouched)
                {
                    touchEvent.Handled = false; // reset 'handled' because it corresponds to another event
                    newElementParent.RaiseTouchEnterEvent(touchEvent);
                }
                newElementParent = newElementParent.VisualParent;
            }
        }

        /// <summary>
        /// Gets the element with which the clickRay intersects, or null if none is found
        /// </summary>
        /// <param name="rootElement">The root <see cref="UIElement"/> from which it should test</param>
        /// <param name="clickRay"><see cref="Ray"/> from the click in object space of the ui component in (-Resolution.X/2 .. Resolution.X/2, -Resolution.Y/2 .. Resolution.Y/2) range</param>
        /// <param name="worldViewProj"></param>
        /// <param name="intersectionPoint">Intersection point between the ray and the element</param>
        /// <returns>The <see cref="UIElement"/> with which the ray intersects</returns>
        public static UIElement GetElementAtScreenPosition(UIElement rootElement, ref Ray clickRay, ref Matrix worldViewProj, ref Vector3 intersectionPoint)
        {
            UIElement clickedElement = null;
            var smallestDepth = float.PositiveInfinity;
            var highestDepthBias = -1.0f;
            PerformRecursiveHitTest(rootElement, ref clickRay, ref worldViewProj, ref clickedElement, ref intersectionPoint, ref smallestDepth, ref highestDepthBias, false);

            return ForceMouseOver ?? clickedElement;
        }

        /// <summary>
        /// Gets the element with which the world Ray intersects, or null if none is found
        /// </summary>
        /// <param name="rootElement">The root <see cref="UIElement"/> from which it should test</param>
        /// <param name="clickRay"><see cref="Ray"/> from the click in world space of the ui component</param>
        /// <param name="worldViewProj"></param>
        /// <param name="intersectionPoint">Intersection point between the ray and the element</param>
        /// <returns>The <see cref="UIElement"/> with which the ray intersects</returns>
        public static UIElement GetElementAtWorldPosition(UIElement rootElement, ref Ray clickRay, ref Matrix worldViewProj, ref Vector3 intersectionPoint)
        {
            UIElement clickedElement = null;
            var smallestDepth = float.PositiveInfinity;
            var highestDepthBias = -1.0f;
            PerformRecursiveHitTest(rootElement, ref clickRay, ref worldViewProj, ref clickedElement, ref intersectionPoint, ref smallestDepth, ref highestDepthBias, true);

            return ForceMouseOver ?? clickedElement;
        }

        /// <summary>
        /// Gets all elements that the given <paramref name="ray"/> intersects.
        /// </summary>
        /// <param name="rootElement">The root <see cref="UIElement"/> from which it should test</param>
        /// <param name="ray"><see cref="Ray"/> from the click in object space of the ui component in (-Resolution.X/2 .. Resolution.X/2, -Resolution.Y/2 .. Resolution.Y/2) range</param>
        /// <param name="worldViewProj"></param>
        /// <returns>A collection of all elements hit by this ray, or an empty collection if no hit.</returns>
        public static ICollection<HitTestResult> GetElementsAtPosition(UIElement rootElement, ref Ray ray, ref Matrix worldViewProj)
        {
            var results = new List<HitTestResult>();
            PerformRecursiveHitTest(rootElement, ref ray, ref worldViewProj, results);
            return results;
        }
        
        private static void PerformRecursiveHitTest(UIElement element, ref Ray ray, ref Matrix worldViewProj, ref UIElement hitElement, ref Vector3 intersectionPoint, ref float smallestDepth, ref float highestDepthBias, bool nonUISpace)
        {
            // if the element is not visible, we also remove all its children
            if (!element.IsVisible)
                return;

            var canBeHit = element.CanBeHitByUser || ForceMouseOver != null;
            if (canBeHit || element.ClipToBounds)
            {
                Vector3 intersection;
                var intersect = element.Intersects(ref ray, out intersection, nonUISpace);

                // don't perform the hit test on children if clipped and parent no hit
                if (element.ClipToBounds && !intersect)
                    return;

                if (canBeHit && intersect)
                {
                    // Calculate the depth of the element with the depth bias so that hit test corresponds to visuals.
                    Vector4 projectedIntersection;
                    var intersection4 = new Vector4(intersection, 1);
                    Vector4.Transform(ref intersection4, ref worldViewProj, out projectedIntersection);
                    var depth = projectedIntersection.Z / projectedIntersection.W;

                    // update the closest element hit
                    if (depth < smallestDepth || (element.DepthBias > highestDepthBias && Math.Abs(depth - smallestDepth) < 0.00001f))
                    {
                        smallestDepth = depth;
                        highestDepthBias = element.DepthBias;
                        intersectionPoint = intersection;
                        hitElement = element;
                    }
                }

                if (ForceMouseOver != null)
                {
                    // clear if we didn't hit anything at all
                    if (!intersect) ForceMouseOver = null;
                    // return as we only check against root when forcing a uielement (e.g. slider we are moving)
                    return;
                }
            }

            // test the children
            foreach (var child in element.HitableChildren)
                PerformRecursiveHitTest(child, ref ray, ref worldViewProj, ref hitElement, ref intersectionPoint, ref smallestDepth, ref highestDepthBias, nonUISpace);
        }

        private static void PerformRecursiveHitTest(UIElement element, ref Ray ray, ref Matrix worldViewProj, ICollection<HitTestResult> results)
        {
            // if the element is not visible, we also remove all its children
            if (!element.IsVisible)
                return;

            var canBeHit = element.CanBeHitByUser;
            if (canBeHit || element.ClipToBounds)
            {
                Vector3 intersection;
                var intersect = element.Intersects(ref ray, out intersection, false);

                // don't perform the hit test on children if clipped and parent no hit
                if (element.ClipToBounds && !intersect)
                    return;

                // Calculate the depth of the element with the depth bias so that hit test corresponds to visuals.
                Vector4 projectedIntersection;
                var intersection4 = new Vector4(intersection, 1);
                Vector4.Transform(ref intersection4, ref worldViewProj, out projectedIntersection);

                // update the hit results
                if (canBeHit && intersect)
                {
                    results.Add(new HitTestResult(element.DepthBias, element, intersection));
                }
            }

            // test the children
            foreach (var child in element.HitableChildren)
                PerformRecursiveHitTest(child, ref ray, ref worldViewProj, results);
        }

        /// <summary>
        /// Represents the result of a hit test on the UI.
        /// </summary>
        public class HitTestResult
        {
            public HitTestResult(float depthBias, UIElement element, Vector3 intersection)
            {
                DepthBias = depthBias;
                Element = element;
                IntersectionPoint = intersection;
            }

            public float DepthBias { get; }

            /// <summary>
            /// Element that was hit.
            /// </summary>
            public UIElement Element { get; }

            /// <summary>
            /// Point of intersection between the ray and the hit element.
            /// </summary>
            public Vector3 IntersectionPoint { get; }
        }
    }
}
