
ifdef VERSION_SUFFIX
VERSION_ARGS=--version-suffix "$(VERSION_SUFFIX)"
endif

all:
	dotnet restore
	cd test/Tmds.Kestrel.Linux.Test ; \
	dotnet test
	dotnet pack src/Tmds.Kestrel.Linux --configuration Release --output . $(VERSION_ARGS)

clean:
	find -name project.lock.json -delete
	find -name bin -type d | xargs rm -rf
	find -name obj -type d | xargs rm -rf
	rm -f *nupkg

