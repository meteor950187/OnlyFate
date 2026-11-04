using OnlyFate.Desktop;
using Xunit;
namespace OnlyFate.Desktop.Tests;
public sealed class PreviewOperationTests
{
 [Fact] public async Task FailedInitializationCanRetryWithoutDuplicatingSuccess()
 {
  var state=new PreviewInitialization();int calls=0;
  Task Init(){calls++;return calls==1?Task.FromException(new InvalidOperationException()):Task.CompletedTask;}
  await Assert.ThrowsAsync<InvalidOperationException>(()=>state.EnsureAsync(Init,TimeSpan.FromSeconds(1),default));
  await state.EnsureAsync(Init,TimeSpan.FromSeconds(1),default);
  await state.EnsureAsync(Init,TimeSpan.FromSeconds(1),default);Assert.Equal(2,calls);
 }
 [Fact] public async Task TimedOutInitializationIsStillSingleFlight()
 {
  var state=new PreviewInitialization();var native=new TaskCompletionSource();int calls=0;
  Task Init(){calls++;return native.Task;}
  await Assert.ThrowsAsync<TimeoutException>(()=>state.EnsureAsync(Init,TimeSpan.Zero,default));
  using var stop=new CancellationTokenSource();stop.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>state.EnsureAsync(Init,TimeSpan.FromSeconds(1),stop.Token));
  Assert.Equal(1,calls);native.SetResult();
  await state.EnsureAsync(Init,TimeSpan.FromSeconds(1),default);Assert.Equal(1,calls);
 }
 [Fact] public async Task CancellationDoesNotReleaseNativePrintGate()
 {
  var state=new PreviewOperationCoordinator();var native=new TaskCompletionSource<int>();using var stop=new CancellationTokenSource();
  var first=state.RunAsync(()=>native.Task,stop.Token);Assert.True(state.IsBusy);
  stop.Cancel();bool secondStarted=false;
  var second=state.RunAsync(()=>{secondStarted=true;return Task.FromResult(2);},default);
  Assert.False(secondStarted);Assert.False(first.IsCompleted);Assert.True(state.IsBusy);
  native.SetResult(1);await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first);
  Assert.Equal(2,await second);Assert.False(state.IsBusy);
 }
 [Fact] public async Task FailedNativePrintReleasesGateAndCancelledWaiterNeverStarts()
 {
  var state=new PreviewOperationCoordinator();var native=new TaskCompletionSource<int>();
  var first=state.RunAsync(()=>native.Task,default);using var stop=new CancellationTokenSource();
  bool started=false;var waiter=state.RunAsync(()=>{started=true;return Task.FromResult(2);},stop.Token);stop.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>waiter);Assert.False(started);
  native.SetException(new InvalidOperationException());await Assert.ThrowsAsync<InvalidOperationException>(()=>first);
  Assert.Equal(3,await state.RunAsync(()=>Task.FromResult(3),default));
 }
}
