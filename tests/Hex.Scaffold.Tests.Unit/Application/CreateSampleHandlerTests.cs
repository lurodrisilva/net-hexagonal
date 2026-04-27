using Hex.Scaffold.Application.Samples.Create;
using Hex.Scaffold.Domain.Ports.Outbound;
using Hex.Scaffold.Domain.SampleAggregate;

namespace Hex.Scaffold.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class CreateSampleHandlerTests
{
  private readonly IRepository<Sample> _repository;
  private readonly ISampleIdGenerator _idGenerator;
  private readonly IMediator _mediator;
  private readonly CreateSampleHandler _handler;

  public CreateSampleHandlerTests()
  {
    _repository = Substitute.For<IRepository<Sample>>();
    _idGenerator = Substitute.For<ISampleIdGenerator>();
    _mediator = Substitute.For<IMediator>();
    _handler = new CreateSampleHandler(
      _repository,
      _idGenerator,
      _mediator,
      Substitute.For<ILogger<CreateSampleHandler>>());
  }

  [Fact]
  public async Task Handle_WithValidCommand_ReturnsSampleId()
  {
    var id = SampleId.From(42);
    var name = SampleName.From("Test Sample");
    var command = new CreateSampleCommand(name, null);
    var expectedSample = new Sample(id, name);

    _idGenerator.NextAsync(Arg.Any<CancellationToken>()).Returns(id);
    _repository.AddAsync(Arg.Any<Sample>(), Arg.Any<CancellationToken>())
      .Returns(Task.FromResult(expectedSample));

    var result = await _handler.Handle(command, CancellationToken.None);

    result.IsSuccess.ShouldBeTrue();
    result.Value.ShouldBe(id);
    await _idGenerator.Received(1).NextAsync(Arg.Any<CancellationToken>());
    await _repository.Received(1).AddAsync(
      Arg.Is<Sample>(s => s.Id == id),
      Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task Handle_WithDescription_SetsDescriptionOnSample()
  {
    var id = SampleId.From(7);
    var name = SampleName.From("Sample With Desc");
    var command = new CreateSampleCommand(name, "some description");
    var capturedSample = default(Sample);

    _idGenerator.NextAsync(Arg.Any<CancellationToken>()).Returns(id);
    _repository.AddAsync(Arg.Any<Sample>(), Arg.Any<CancellationToken>())
      .Returns(callInfo =>
      {
        capturedSample = callInfo.Arg<Sample>();
        return Task.FromResult(capturedSample);
      });

    await _handler.Handle(command, CancellationToken.None);

    capturedSample.ShouldNotBeNull();
    capturedSample!.Id.ShouldBe(id);
    capturedSample.Description.ShouldBe("some description");
  }
}
