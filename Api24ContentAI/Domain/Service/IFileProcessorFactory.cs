namespace Api24ContentAI.Domain.Service;

public interface IFileProcessorFactory
{
    IFileProcessor GetProcessor(string fileName);
}