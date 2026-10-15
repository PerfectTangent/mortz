#!/usr/bin/env bash
# Rebuilds the open-source GameNetworkingSockets binaries committed under native/gns/.
#
#   tools/native/build-gns.sh [linux|windows|all]   (default: all)
#
# Needs only Docker on the host. Each target builds in its own container so the output does not
# depend on the host toolchain:
#   linux   - Steam Runtime "sniper" SDK (glibc 2.31), so the .so runs on Debian bookworm and SteamOS.
#   windows - Debian bookworm, MinGW-w64 (posix threads) cross-compile.
# Everything is linked statically (OpenSSL, protobuf, abseil, libstdc++), so the .so needs only
# libc/libm and the .dll only system DLLs. The gates at the end of each build enforce that.
set -euo pipefail

GNS_TAG=v1.6.1
GNS_COMMIT=d534c19
# The vcpkg baseline GNS v1.6.1 pins in its vcpkg.json.
VCPKG_COMMIT=522253caf47268c1724f486a035e927a42a90092
GLIBC_MAX=2.31

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$repo_root/build/native-gns"

# Inside the container: fetch sources, configure, build, strip, check.
inside() {
	local target="$1"
	cd /work
	if [[ ! -d gns ]]; then
		git clone --branch "$GNS_TAG" https://github.com/ValveSoftware/GameNetworkingSockets gns
	fi
	[[ "$(git -C gns rev-parse --short=7 HEAD)" == "$GNS_COMMIT" ]] || { echo "gns is not at $GNS_COMMIT" >&2; exit 1; }
	if [[ ! -d vcpkg ]]; then
		# Full clone: a shallow one cannot check out the manifest baseline.
		git clone https://github.com/microsoft/vcpkg vcpkg
	fi
	git -C vcpkg checkout -q "$VCPKG_COMMIT"
	[[ -x vcpkg/vcpkg ]] || vcpkg/bootstrap-vcpkg.sh -disableMetrics
	export VCPKG_DOWNLOADS=/work/downloads
	export VCPKG_DEFAULT_BINARY_CACHE=/work/bincache
	mkdir -p "$VCPKG_DOWNLOADS" "$VCPKG_DEFAULT_BINARY_CACHE"

	local common=(-S gns -G Ninja -DCMAKE_BUILD_TYPE=Release
		-DCMAKE_TOOLCHAIN_FILE=/work/vcpkg/scripts/buildsystems/vcpkg.cmake
		-DBUILD_SHARED_LIB=ON -DBUILD_STATIC_LIB=OFF -DProtobuf_USE_STATIC_LIBS=ON)

	case "$target" in
	linux)
		rm -rf build-linux
		cmake "${common[@]}" -B build-linux -DVCPKG_TARGET_TRIPLET=x64-linux \
			"-DCMAKE_SHARED_LINKER_FLAGS=-Wl,--exclude-libs,ALL -static-libstdc++ -static-libgcc"
		ninja -C build-linux
		local so=build-linux/bin/libGameNetworkingSockets.so
		strip "$so"
		mkdir -p out/linux-x64 && cp "$so" out/linux-x64/

		local symbols glibc deps
		symbols=$(nm -D --defined-only "$so" | wc -l)
		glibc=$(objdump -T "$so" | grep -o 'GLIBC_[0-9.]*' | sed 's/GLIBC_//' | sort -V | tail -1)
		deps=$(objdump -p "$so" | awk '/NEEDED/ {print $2}' | tr '\n' ' ')
		echo "linux: $(stat -c %s "$so") bytes, $symbols exported symbols, glibc floor $glibc, needs: $deps"
		(( symbols < 200 )) || { echo "gate: too many exported symbols ($symbols), static deps leaked" >&2; exit 1; }
		[[ "$(printf '%s\n%s\n' "$glibc" "$GLIBC_MAX" | sort -V | tail -1)" == "$GLIBC_MAX" ]] \
			|| { echo "gate: glibc floor $glibc > $GLIBC_MAX" >&2; exit 1; }
		for d in $deps; do
			[[ "$d" =~ ^(libc|libm|libpthread|libdl|librt|ld-linux-x86-64)\.so ]] \
				|| { echo "gate: unexpected dependency $d" >&2; exit 1; }
		done
		;;
	windows)
		# GNS links -lIphlpapi; MinGW ships libiphlpapi.a and the filesystem is case-sensitive.
		mkdir -p mingwfix
		ln -sf /usr/x86_64-w64-mingw32/lib/libiphlpapi.a mingwfix/libIphlpapi.a
		rm -rf build-mingw
		cmake "${common[@]}" -B build-mingw -DCMAKE_SYSTEM_NAME=Windows \
			-DCMAKE_C_COMPILER=x86_64-w64-mingw32-gcc -DCMAKE_CXX_COMPILER=x86_64-w64-mingw32-g++ \
			-DCMAKE_RC_COMPILER=x86_64-w64-mingw32-windres \
			-DVCPKG_TARGET_TRIPLET=x64-mingw-static -DVCPKG_HOST_TRIPLET=x64-linux \
			`# The app-local step copies dependency DLLs with PowerShell; everything is static here.` \
			-DVCPKG_APPLOCAL_DEPS=OFF \
			"-DCMAKE_SHARED_LINKER_FLAGS=-L/work/mingwfix -static-libstdc++ -static-libgcc -Wl,-Bstatic,--whole-archive -lwinpthread -Wl,--no-whole-archive,-Bdynamic"
		ninja -C build-mingw
		local dll
		dll=$(find build-mingw -name 'libGameNetworkingSockets.dll' -o -name 'GameNetworkingSockets.dll' | head -1)
		x86_64-w64-mingw32-strip "$dll"
		mkdir -p out/win-x64 && cp "$dll" out/win-x64/GameNetworkingSockets.dll

		local imports
		imports=$(x86_64-w64-mingw32-objdump -p "$dll" | awk '/DLL Name/ {print $3}' | tr '\n' ' ')
		echo "windows: $(stat -c %s "$dll") bytes, imports: $imports"
		for d in $imports; do
			[[ "${d,,}" =~ ^(kernel32|ws2_32|iphlpapi|advapi32|crypt32|user32|bcrypt|msvcrt|winmm|dbghelp|api-ms-win-.*)\.dll$ ]] \
				|| { echo "gate: unexpected import $d" >&2; exit 1; }
		done
		;;
	esac
}

if [[ "${1:-}" == "--inside" ]]; then
	inside "$2"
	exit 0
fi

# On the host: build the images, run the containers, copy the results into native/gns/.
build_image() {
	local tag="$1" base="$2" extra="$3" post="${4:-true}"
	docker build -t "$tag" - <<EOF
FROM $base
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
	build-essential git curl zip unzip tar pkg-config perl make python3 python3-pip ca-certificates \
	linux-libc-dev binutils $extra && rm -rf /var/lib/apt/lists/*
RUN pip3 install --no-cache-dir cmake==3.31.6 ninja==1.11.1.4 || pip3 install --no-cache-dir --break-system-packages cmake==3.31.6 ninja==1.11.1.4
RUN $post
EOF
}

run_target() {
	local target="$1" image="$2"
	mkdir -p "$work/$target"
	docker run --rm --user "$(id -u):$(id -g)" -e HOME=/work \
		-v "$work/$target:/work" -v "$repo_root/tools/native/build-gns.sh:/build-gns.sh:ro" \
		"$image" bash /build-gns.sh --inside "$target"
}

what="${1:-all}"
if [[ "$what" == linux || "$what" == all ]]; then
	build_image mortz-gns-linux registry.gitlab.steamos.cloud/steamrt/sniper/sdk:latest ""
	run_target linux mortz-gns-linux
	mkdir -p "$repo_root/native/gns/linux-x64"
	cp "$work/linux/out/linux-x64/libGameNetworkingSockets.so" "$repo_root/native/gns/linux-x64/"
fi
if [[ "$what" == windows || "$what" == all ]]; then
	# vcpkg builds the dependencies with the plain compiler names; Debian points those at the
	# win32-threads variant, which has no std::mutex.
	build_image mortz-gns-windows debian:bookworm "mingw-w64" \
		"update-alternatives --set x86_64-w64-mingw32-gcc /usr/bin/x86_64-w64-mingw32-gcc-posix && update-alternatives --set x86_64-w64-mingw32-g++ /usr/bin/x86_64-w64-mingw32-g++-posix"
	run_target windows mortz-gns-windows
	mkdir -p "$repo_root/native/gns/win-x64"
	cp "$work/windows/out/win-x64/GameNetworkingSockets.dll" "$repo_root/native/gns/win-x64/"
fi
echo "done: $(ls -l "$repo_root"/native/gns/*/* 2>/dev/null)"
