using Hex.Scaffold.Application.Samples.Create;
using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class CreateSampleHandlerTests
{
  private readonly IRepository<Sample> _repository;
  private readonly IMediator _mediator;
  private readonly CreateSampleHandler _handler;

  public CreateSampleHandlerTests()
  {
    _repository = Substitute.For<IRepository<Sample>>();
    _mediator = Substitute.For<IMediator>();
    _handler = new CreateSampleHandler(
      _repository,
      _mediator,
      Substitute.For<ILogger<CreateSampleHandler>>());
  }

  [Fact]
  public async Task Handle_WithValidCommand_ReturnsSampleId()
  {
    var name = SampleName.From("Test Sample");
    var command = new CreateSampleCommand(name, null);
    var expectedSample = new Sample(name);

    _repository.AddAsync(Arg.Any<Sample>(), Arg.Any<CancellationToken>())
      .Returns(Task.FromResult(expectedSample));

    var result = await _handler.Handle(command, CancellationToken.None);

    result.IsSuccess.ShouldBeTrue();
    await _repository.Received(1).AddAsync(Arg.Any<Sample>(), Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task Handle_WithDescription_SetsDescriptionOnSample()
  {
    var name = SampleName.From("Sample With Desc");
    var command = new CreateSampleCommand(name, "some description");
    var capturedSample = default(Sample);

    _repository.AddAsync(Arg.Any<Sample>(), Arg.Any<CancellationToken>())
      .Returns(callInfo =>
      {
        capturedSample = callInfo.Arg<Sample>();
        return Task.FromResult(capturedSample);
      });

    await _handler.Handle(command, CancellationToken.None);

    capturedSample.ShouldNotBeNull();
    capturedSample!.Description.ShouldBe("some description");
  }
}
